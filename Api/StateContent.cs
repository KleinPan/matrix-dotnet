using System.Text.Json.Nodes;

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
