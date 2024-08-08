using System.Text.Json.Serialization;

namespace matrix_dotnet.Api;

public record RoomSummary(
	[property: JsonPropertyName("m.heroes")] // Why??
		string[] heroes,
	[property: JsonPropertyName("m.invited_member_count")]
		int invited_member_count,
	[property: JsonPropertyName("m.joined_member_count")]
		int joined_member_count
);

public record UnreadNotificationCounts(
	int highlight_count,
	int notification_count
);

public record InvitedRoom(InviteState invite_state);
public record JoinedRoom(
	AccountData account_data,
	Ephemeral ephemeral,
	State state,
	RoomSummary summary,
	Timeline timeline,
	UnreadNotificationCounts unread_notifications,
	Dictionary<string, UnreadNotificationCounts>? unread_thread_notifications
);
public record KnockedRoom(KnockState knock_state);
public record LeftRoom(AccountData account_data, State state, Timeline timeline);

public record Rooms(
	Dictionary<RoomID, InvitedRoom>? invite,
	Dictionary<RoomID, JoinedRoom>? join,
	Dictionary<RoomID, KnockedRoom>? knock,
	Dictionary<RoomID, LeftRoom>? leave
);

public record RoomCreationStateEvent(EventContent content, string state_key, string type) : Event(content, type, state_key, null, null);
public enum RoomPreset { private_chat, public_chat, trusted_private_chat }
public enum RoomVisibility { Public, Private }
public record RoomCreationRequest(
	RoomCreation? creation_content = null,
	RoomCreationStateEvent[]? initial_state = null,
	string[]? invite = null,
	// Invite3pid invite_3pid, // NOT IMPLEMENTED
	bool? is_direct = null,
	string? name = null,
	PowerLevels? power_level_content_override = null,
	RoomPreset? preset = null,
	string? room_alias_name = null,
	string? room_version = null,
	string? topic = null,
	RoomVisibility? visibility = null
);

public record RoomCreationResponse(RoomID room_id);

public record InviteRequest(string user_id, string? reason = null);

public record JoinRequest(string? reason = null); // 3pid NOT IMPLEMENTED

public record JoinResponse(RoomID room_id);

public record LeaveRequest(string? reason = null);
