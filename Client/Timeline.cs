using System.Collections.Immutable;

namespace matrix_dotnet.Client;

using StateDict = ImmutableDictionary<StateKey, Api.EventContent>;
public record StateKey(string type, string state_key);

public record EventWithState(
	Api.Event Event,
	StateDict State
) {
	public Api.RoomMember? GetSender() {
		if (Event.sender is null || !State.TryGetValue(new StateKey("m.room.member", Event.sender), out var member)) return null;
		return (Api.RoomMember?)member;
	}
};

internal record TimelinePoint(
	EventWithState? Event,
	string? From,
	string? To
) {
	public bool IsHole => Event is null;
};

internal class TimelineEvent : ITimelineEvent {
	public EventWithState Value {
		get {
			CheckOrphan();
			if (Node.Value.Event is null) throw new ArgumentNullException("TimelineEvent instantiated with hole node");
			return Node.Value.Event;
		}
	}
	private MatrixClient Client;
	private string RoomId;
	
	internal void RemoveSelf() {
		if (Node.List is null) return;
		Node.List.Remove(Node);
	}

	private LinkedListNode<TimelinePoint> Node;
	public TimelineEvent(LinkedListNode<TimelinePoint> node, MatrixClient client, string roomId) {
		if (node.Value.Event is null) throw new ArgumentNullException("TimelineEvent instantiated with hole node");
		if (node.List is null) throw new ArgumentNullException("TimelineEvent instantiated with orphan node");
		Node = node;
		Client = client;
		RoomId = roomId;
	}
	
	private void CheckOrphan() {
		if (Node.List is null) {
			if (Node.Value.Event is null) throw new ArgumentNullException("TimelineEvent instantiated with hole node");
			string? event_id = Node.Value.Event.Event.event_id;
			if (event_id is null) throw new ArgumentNullException("Orphaned node has no event_id");
			Node = ((TimelineEvent)Client.EventsById[event_id]).Node;
			if (Node.Value.Event is null) throw new ArgumentNullException("TimelineEvent instantiated with hole node");
		}
	}

	public async Task<ITimelineEvent?> Next() {
		CheckOrphan();

		if (Node.Next is null) return null;
		if (Node.Next.Value.Event is not null) return new TimelineEvent(Node.Next, Client, RoomId);

		Client.FillLock();
		if (Node.Next.Value.Event is not null) {
			Client.FillUnlock();
			return new TimelineEvent(Node.Next, Client, RoomId);
		}

		var hole = Node.Next.Value;
		var response = await Retry.RetryAsync(async () => await Client.ApiClient.GetRoomMessages(RoomId, Api.Dir.f, from: hole.From, to: hole.To));


		var state = Value.State;
		if (response.state is not null)
			state = MatrixClient.Resolve(response.state, state).state;

		var newMessages = MatrixClient.Resolve(response.chunk, state).events;

		Node.List!.Remove(Node.Next);

		if (response.end is not null)
			Node.List.AddAfter(Node, new TimelinePoint(null, response.end, hole.To));

		foreach (var message in newMessages.Reverse()) {
			var point = new TimelinePoint(message, null, null);
			Node.List.AddAfter(Node, point);
			Client.Deduplicate(new TimelineEvent(Node.Next, Client, RoomId));
		}

		var next = Node.Next;

		Client.FillUnlock();

		if (newMessages.Count() == 0) return null;

		return new TimelineEvent(next, Client, RoomId);
	}

	public async Task<ITimelineEvent?> Previous() {
		CheckOrphan();

		if (Node.Previous is null) return null;
		if (Node.Previous.Value.Event is not null) return new TimelineEvent(Node.Previous, Client, RoomId);

		Client.FillLock();
		if (Node.Previous.Value.Event is not null) {
			Client.FillUnlock();
			return new TimelineEvent(Node.Previous, Client, RoomId);
		}

		var hole = Node.Previous.Value;
		var response = await Retry.RetryAsync(async () => await Client.ApiClient.GetRoomMessages(RoomId, Api.Dir.b, from: hole.To, to: hole.From));

		var state = Value.State;
		if (response.state is not null)
			state = MatrixClient.Resolve(response.state, state).state;

		var newMessages = MatrixClient.Resolve(response.chunk, state, rewind: true).events;

		Node.List!.Remove(Node.Previous);

		if (response.end is not null)
			Node.List.AddBefore(Node, new TimelinePoint(null, hole.From, response.end));

		foreach (var message in newMessages.Reverse()) {
			var point = new TimelinePoint(message, null, null);
			Node.List.AddBefore(Node, point);
			Client.Deduplicate(new TimelineEvent(Node.Previous, Client, RoomId));
		}

		var previous = Node.Previous;
		
		Client.FillUnlock();

		if (newMessages.Count() == 0) return null;

		return new TimelineEvent(previous, Client, RoomId);
	}

	public ITimelineEvent? NextSync() {
		if (Node.Next is null) return null;
		if (Node.Next.Value.Event is not null) return new TimelineEvent(Node.Next, Client, RoomId);
		return null;
	}

	public ITimelineEvent? PreviousSync() {
		if (Node.Previous is null) return null;
		if (Node.Previous.Value.Event is not null) return new TimelineEvent(Node.Previous, Client, RoomId);
		return null;
	}
};
public interface ITimelineEvent {
	public EventWithState Value { get; }
	public Task<ITimelineEvent?> Next();
	public Task<ITimelineEvent?> Previous();
	public ITimelineEvent? NextSync();
	public ITimelineEvent? PreviousSync();
	public async IAsyncEnumerable<ITimelineEvent> EnumerateForward() {
		ITimelineEvent? current = this;
		do {
			yield return current;
			current = await current.Next();
		} while (current is not null);
	}
	public async IAsyncEnumerable<ITimelineEvent> EnumerateBackward() {
		ITimelineEvent? current = this;
		do {
			yield return current;
			current = await current.Previous();
		} while (current is not null);
	}
	public IEnumerable<ITimelineEvent> EnumerateForwardSync() {
		ITimelineEvent? current = this;
		do {
			yield return current;
			current = current.NextSync();
		} while (current is not null);
	}
	public IEnumerable<ITimelineEvent> EnumerateBackwardSync() {
		ITimelineEvent? current = this;
		do {
			yield return current;
			current = current.PreviousSync();
		} while (current is not null);
	}
}

public class Timeline {
	// TODO: lock linked list
	private LinkedList<TimelinePoint> EventList = new();

	private MatrixClient Client;
	private string RoomId;

	public ITimelineEvent? First {
		get {
			LinkedListNode<TimelinePoint>? node = EventList.First;
			if (node is null) return null;
			while (node.Value.Event is null) {
				node = node.Next;
				if (node is null) throw new Exception("Timeline is only holes. This should not happen.");
			}
			return new TimelineEvent(node, Client, RoomId);
		}
	}

	public ITimelineEvent? Last {
		get {
			LinkedListNode<TimelinePoint>? node = EventList.Last;
			if (node is null) return null;
			while (node.Value.Event is null) {
				node = node.Previous;
				if (node is null) throw new Exception("Timeline is only holes. This should not happen.");
			}
			return new TimelineEvent(node, Client, RoomId);
		}
	}

	public void Sync(Api.Timeline timeline, StateDict? state, string prev_batch, string? original_batch) {
		if (prev_batch != original_batch) {
			EventList.AddLast(new TimelinePoint(null, original_batch, prev_batch));
		}

		var resolvedEvents = MatrixClient.Resolve(timeline.events, state).events;

		foreach (var ev in resolvedEvents) {
			EventList.AddLast(new TimelinePoint(ev, null, null));
		}
	}

	public Timeline(MatrixClient client, string roomId) {
		Client = client;
		RoomId = roomId;
	}

}

public record JoinedRoom(
	StateDict account_data,
	StateDict ephemeral,
	StateDict state,
	Api.RoomSummary summary,
	Timeline timeline,
	Api.UnreadNotificationCounts unread_notifications,
	Dictionary<string, Api.UnreadNotificationCounts> unread_thread_notifications
);

public record LeftRoom(
	StateDict account_data,
	StateDict state,
	Timeline timeline
);

