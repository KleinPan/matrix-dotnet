using System.Collections.Immutable;

namespace matrix_dotnet.Client;

using StateDict = ImmutableDictionary<StateKey, Api.EventContent>;
public record StateKey(string type, string state_key);

public record EventWithState(
	Api.ClientEvent Event,
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
		internal set {
			CheckOrphan();
			Node.Value = new TimelinePoint(value, null, null);
		}
	}
	private Timeline Timeline;

	internal void RemoveSelf() {
		if (Node.List is null) return;
		Node.List.Remove(Node);
	}

	private LinkedListNode<TimelinePoint> Node;
	public TimelineEvent(LinkedListNode<TimelinePoint> node, Timeline timeline) {
		if (node.Value.Event is null) throw new ArgumentNullException("TimelineEvent instantiated with hole node");
		if (node.List is null) throw new ArgumentNullException("TimelineEvent instantiated with orphan node");
		Node = node;
		Timeline = timeline;
	}

	private void CheckOrphan() {
		if (Node.List is null) {
			if (Node.Value.Event is null) throw new ArgumentNullException("TimelineEvent instantiated with hole node");
			Api.EventID? event_id = Node.Value.Event.Event.event_id;
			if (event_id is null) throw new ArgumentNullException("Orphaned node has no event_id");
			Node = ((TimelineEvent)Timeline.Client.EventsById[event_id.Value]).Node;
			if (Node.Value.Event is null) throw new ArgumentNullException("TimelineEvent instantiated with hole node");
		}
	}

	public async Task<ITimelineEvent?> Next() {
		CheckOrphan();

		if (Node.Next is null) return null;
		if (Node.Next.Value.Event is not null) return new TimelineEvent(Node.Next, Timeline);

		Timeline.Client.FillLock();
		if (Node.Next.Value.Event is not null) {
			Timeline.Client.FillUnlock();
			return new TimelineEvent(Node.Next, Timeline);
		}

		var hole = Node.Next.Value;
		var response = await Retry.RetryAsync(async () => await Timeline.Client.ApiClient.GetRoomMessages(Timeline.RoomId, Api.Dir.f, from: hole.From, to: hole.To));


		var state = Value.State;
		if (response.state is not null)
			state = MatrixClient.Resolve(response.state, state).state;

		var newMessages = MatrixClient.Resolve(response.chunk, state).events;

		Node.List!.Remove(Node.Next);

		if (response.end is not null)
			Node.List.AddAfter(Node, new TimelinePoint(null, response.end, hole.To));

		foreach (var message in newMessages.Reverse()) {
			Timeline.BeforeAddingEvent(message);
			var point = new TimelinePoint(message, null, null);
			Node.List.AddAfter(Node, point);
			Timeline.Client.Deduplicate(new TimelineEvent(Node.Next, Timeline));
		}

		var next = Node.Next;

		Timeline.Client.FillUnlock();

		if (newMessages.Count() == 0) return null;

		return new TimelineEvent(next, Timeline);
	}

	public async Task<ITimelineEvent?> Previous() {
		CheckOrphan();

		if (Node.Previous is null) return null;
		if (Node.Previous.Value.Event is not null) return new TimelineEvent(Node.Previous, Timeline);

		Timeline.Client.FillLock();
		if (Node.Previous.Value.Event is not null) {
			Timeline.Client.FillUnlock();
			return new TimelineEvent(Node.Previous, Timeline);
		}

		var hole = Node.Previous.Value;
		var response = await Retry.RetryAsync(async () => await Timeline.Client.ApiClient.GetRoomMessages(Timeline.RoomId, Api.Dir.b, from: hole.To, to: hole.From));

		var state = Value.State;
		if (response.state is not null)
			state = MatrixClient.Resolve(response.state, state).state;

		var newMessages = MatrixClient.Resolve(response.chunk, state, rewind: true).events;

		Node.List!.Remove(Node.Previous);

		if (response.end is not null)
			Node.List.AddBefore(Node, new TimelinePoint(null, hole.From, response.end));

		foreach (var message in newMessages.Reverse()) {
			Timeline.BeforeAddingEvent(message);
			var point = new TimelinePoint(message, null, null);
			Node.List.AddBefore(Node, point);
			Timeline.Client.Deduplicate(new TimelineEvent(Node.Previous, Timeline));
		}

		var previous = Node.Previous;

		Timeline.Client.FillUnlock();

		if (newMessages.Count() == 0) return null;

		return new TimelineEvent(previous, Timeline);
	}

	public ITimelineEvent? NextOffline() {
		if (Node.Next is null) return null;
		if (Node.Next.Value.Event is not null) return new TimelineEvent(Node.Next, Timeline);
		return null;
	}

	public ITimelineEvent? PreviousOffline() {
		if (Node.Previous is null) return null;
		if (Node.Previous.Value.Event is not null) return new TimelineEvent(Node.Previous, Timeline);
		return null;
	}
};

/// <summary> The linked list node in a <see cref="Timeline"/> </summary>
public interface ITimelineEvent {
	public EventWithState Value { get; }
	/// <summary> Get the next event in the list. Automatically
	/// fetches new events when applicable. </summary>
	public Task<ITimelineEvent?> Next();
	/// <summary> Get the previous event in the list. Automatically
	/// fetches new events when applicable. </summary>
	public Task<ITimelineEvent?> Previous();
	/// <summary> Get the next event in the list. Only gives
	/// already cached events. </summary>
	public ITimelineEvent? NextOffline();
	/// <summary> Get the previous event in the list. Only gives
	/// already cached events. </summary>
	public ITimelineEvent? PreviousOffline();
	/// <summary> Get an enumerable starting with this event going forward.
	/// Automatically fetches new events when applicable.</summary>
	public async IAsyncEnumerable<ITimelineEvent> EnumerateForward() {
		ITimelineEvent? current = this;
		do {
			yield return current;
			current = await current.Next();
		} while (current is not null);
	}
	/// <summary> Get an enumerable starting with this event going backward.
	/// Automatically fetches new events when applicable.</summary>
	public async IAsyncEnumerable<ITimelineEvent> EnumerateBackward() {
		ITimelineEvent? current = this;
		do {
			yield return current;
			current = await current.Previous();
		} while (current is not null);
	}
	/// <summary> Get an enumerable starting with this event going forward.
	/// Only gives already cached events. </summary>
	public IEnumerable<ITimelineEvent> EnumerateForwardOffline() {
		ITimelineEvent? current = this;
		do {
			yield return current;
			current = current.NextOffline();
		} while (current is not null);
	}
	/// <summary> Get an enumerable starting with this event going backward.
	/// Only gives already cached events. </summary>
	public IEnumerable<ITimelineEvent> EnumerateBackwardOffline() {
		ITimelineEvent? current = this;
		do {
			yield return current;
			current = current.PreviousOffline();
		} while (current is not null);
	}
}

/// <summary> Represents the history of a joined room in a linked list. </summary>
public class Timeline {
	private LinkedList<TimelinePoint> EventList = new();

	internal MatrixClient Client { get; private set; }
	internal Api.RoomID RoomId { get; private set; }

	/// <summary> Get the first cached event in this timeline.
	/// To get earlier events from the server, use <see cref="ITimelineEvent.Previous"/> </summary>
	public ITimelineEvent? First {
		get {
			LinkedListNode<TimelinePoint>? node = EventList.First;
			if (node is null) return null;
			while (node.Value.Event is null) {
				node = node.Next;
				if (node is null) throw new Exception("Timeline is only holes. This should not happen.");
			}
			return new TimelineEvent(node, this);
		}
	}

	/// <summary> Get the last cached event in this timeline.
	/// To get later events from the server, use <see cref="ITimelineEvent.Next"/>
	/// or more often <see cref="Client.MatrixClient.Sync"/> </summary>
	public ITimelineEvent? Last {
		get {
			LinkedListNode<TimelinePoint>? node = EventList.Last;
			if (node is null) return null;
			while (node.Value.Event is null) {
				node = node.Previous;
				if (node is null) throw new Exception("Timeline is only holes. This should not happen.");
			}
			return new TimelineEvent(node, this);
		}
	}

	internal void Sync(Api.Timeline timeline, StateDict? state, string prev_batch, string? original_batch) {
		if (prev_batch != original_batch) {
			EventList.AddLast(new TimelinePoint(null, original_batch, prev_batch));
		}

		var resolvedEvents = MatrixClient.Resolve(timeline.events, state).events;

		foreach (var ev in resolvedEvents) {
			EventList.AddLast(new TimelinePoint(ev, null, null));
			BeforeAddingEvent(ev);
		}
	}

	internal void BeforeAddingEvent(EventWithState eventWithState) {
		if (eventWithState.Event.content is Api.Redaction) {
			Client.RedactEvent(eventWithState.Event);
		}
	}


	internal Timeline(MatrixClient client, Api.RoomID roomId) {
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

