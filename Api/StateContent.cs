using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace matrix_dotnet.Api;

public enum Membership { invite, join, knock, leave, ban };

public record RoomMember(
	Uri? avatar_url,
	string? displayname,
	bool? is_direct,
	string? join_authorised_via_users_server,
	Membership membership,
	string reason,
	JsonObject third_party_invite // NOT SUPPORTED
) : EventContent();

public record Redaction(EventID redacts, string? reason = null) : EventContent();

public record CanonicalAlias(string alias, string[] alt_aliases) : EventContent();

public record PreviousRoom(EventID event_id, RoomID room_id);
public record RoomCreation(
	string? creator = null,
	PreviousRoom? predecessor = null,
	string? type = null,
	string? room_version = null,
	[property: JsonPropertyName("m.federate")]
	bool? federate = null
) : EventContent();

public enum JoinRule { Public, Knock, Invite, Private, Restricted, Knock_restricted }
public record AllowCondition(RoomID room_id, string type);
public record JoinRules(
	JoinRule join_rule,
	AllowCondition[] allow
) : EventContent();

public record NotificationsPowerLevels(long room);
public record PowerLevels(
	long ban,
	Dictionary<string,long> events,
	long events_default,
	long invite,
	long kick,
	NotificationsPowerLevels notifications,
	long redact,
	long state_default,
	Dictionary<string,long> users,
	long users_default
);
