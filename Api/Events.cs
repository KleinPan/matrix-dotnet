namespace matrix_dotnet.Api;

[JsonPropertyPolymorphic(typeof(EventContent), TypeDiscriminatorPropertyName = "type", DefaultType = typeof(UnknownEventContent))]
[JsonPropertyDerivedType(typeof(Message), typeDiscriminator: "m.room.message")]
[JsonPropertyDerivedType(typeof(Redaction), typeDiscriminator: "m.room.redaction")]
[JsonPropertyDerivedType(typeof(RoomMember), typeDiscriminator: "m.room.member")]
public record Event([JsonPropertyTargetProperty] EventContent? content, string type, string? state_key, string? sender, EventID? event_id) {
	public bool IsState { get { return state_key is not null; } }
};

public record StrippedStateEvent(
	[JsonPropertyTargetProperty]
	EventContent? content, // Redacted events have no content
	string sender,
	string state_key,
	string type
) : Event(content, type, state_key, sender, null);

public record ClientEvent(
	[JsonPropertyTargetProperty]
	EventContent? content,
	EventID? event_id,
	long origin_server_ts,
	RoomID? room_id,
	string sender,
	string? state_key,
	[JsonPropertyRecursive]
	UnsignedData? unsigned,
	string type
) : Event(content, type, state_key, sender, event_id) {
	public bool IsRedacted => unsigned?.redacted_because is not null && content is null;
};

public record ClientEventWithoutRoomID(
	[JsonPropertyTargetProperty]
	EventContent? content,
	EventID? event_id,
	long origin_server_ts,
	string sender,
	string? state_key,
	[JsonPropertyRecursive]
	UnsignedData? unsigned,
	string type
) : ClientEvent(content, event_id, origin_server_ts, null, sender, state_key, unsigned, type);

public record UnsignedData(
	int? age = null,
	string? membership = null,
	[JsonPropertyTargetProperty]
	EventContent? prev_content = null,
	ClientEvent? redacted_because = null,
	string? transaction_id = null
);


public abstract record EventList<TEvent>(TEvent[] events) where TEvent : Event;
public record AccountData(Event[] events) : EventList<Event>(events);
public record Presence(Event[] events) : EventList<Event>(events);
public record ToDevice(Event[] events) : EventList<Event>(events);
public record Ephemeral(Event[] events) : EventList<Event>(events);
public record InviteState(StrippedStateEvent[] events) : EventList<StrippedStateEvent>(events);
public record KnockState(StrippedStateEvent[] events) : EventList<StrippedStateEvent>(events);
public record State(ClientEventWithoutRoomID[] events) : EventList<ClientEventWithoutRoomID>(events);
public record Timeline(
	ClientEventWithoutRoomID[] events,
	bool limited,
	string prev_batch
) : EventList<ClientEventWithoutRoomID>(events);

