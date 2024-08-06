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
	public EventWithState Event { get; private set; }
	private MatrixClient Client;
	private string RoomId;

	private LinkedListNode<TimelinePoint> Node;
	public TimelineEvent(LinkedListNode<TimelinePoint> node, MatrixClient client, string roomId) {
		if (node.Value.Event is null) throw new ArgumentNullException("TimelineEvent instantiated with hole node");
		if (node.List is null) throw new ArgumentNullException("TimelineEvent instantiated with orphan node");
		Event = node.Value.Event;
		Node = node;
		Client = client;
		RoomId = roomId;
	}

	public async Task<ITimelineEvent?> Next() {
		if (Node.Next is null) return null;
		if (Node.Next.Value.Event is not null) return new TimelineEvent(Node.Next, Client, RoomId);

		var hole = Node.Next.Value;
		var response = await Retry.RetryAsync(async () => await Client.ApiClient.GetRoomMessages(RoomId, Api.Dir.f, from: hole.From, to: hole.To));


		var state = Event.State;
		if (response.state is not null)
			state = MatrixClient.Resolve(response.state, state).LastOrDefault()?.State;

		var newMessages = MatrixClient.Resolve(response.chunk, state);

		Node.List!.Remove(Node.Next);

		if (response.end is not null)
			Node.List.AddAfter(Node, new TimelinePoint(null, response.end, hole.To));

		foreach (var message in newMessages.Reverse()) {
			Node.List.AddAfter(Node, new TimelinePoint(message, null, null));
		}

		if (newMessages.Count() == 0) return null;

		return new TimelineEvent(Node.Next, Client, RoomId);
	}

	public async Task<ITimelineEvent?> Previous() {
		if (Node.Previous is null) return null;
		if (Node.Previous.Value.Event is not null) return new TimelineEvent(Node.Previous, Client, RoomId);

		var hole = Node.Previous.Value;
		var response = await Retry.RetryAsync(async () => await Client.ApiClient.GetRoomMessages(RoomId, Api.Dir.b, from: hole.To, to: hole.From));

		var state = Event.State;
		if (response.state is not null)
			state = MatrixClient.Resolve(response.state, state).LastOrDefault()?.State;

		var newMessages = MatrixClient.Resolve(response.chunk, state, rewind: true);

		Node.List!.Remove(Node.Previous);

		if (response.end is not null)
			Node.List.AddBefore(Node, new TimelinePoint(null, hole.From, response.end));

		foreach (var message in newMessages.Reverse()) {
			Node.List.AddBefore(Node, new TimelinePoint(message, null, null));
		}

		if (newMessages.Count() == 0) return null;

		return new TimelineEvent(Node.Previous, Client, RoomId);
	}

};
public interface ITimelineEvent {
	public EventWithState Event { get; }
	public Task<ITimelineEvent?> Next();
	public Task<ITimelineEvent?> Previous();
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

	public void Sync(Api.Timeline timeline, StateDict? state, bool isGapped) {
		if (isGapped) {
			if (EventList.Last is not null && EventList.Last.Value.IsHole) {
				EventList.Last.Value = new TimelinePoint(null, EventList.Last.Value.From, timeline.prev_batch);
			} else {
				EventList.AddLast(new TimelinePoint(null, null, timeline.prev_batch));
			}
		}

		var resolvedEvents = MatrixClient.Resolve(timeline.events, state);

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



