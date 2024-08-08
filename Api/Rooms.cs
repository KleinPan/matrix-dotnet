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

