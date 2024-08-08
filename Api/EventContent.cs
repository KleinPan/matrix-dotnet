using System.Text.Json.Serialization;

namespace matrix_dotnet.Api;

/// <summary><see cref="IMatrixApi.SendEvent"/></summary>
public record SendEventResponse(EventID event_id);
public record RedactResponse(EventID event_id);

[JsonPropertyPolymorphic(typeof(EventContent), TypeDiscriminatorPropertyName = "type", DefaultType = typeof(UnknownEventContent))]
[JsonPropertyDerivedType(typeof(Message), typeDiscriminator: "m.room.message")]
[JsonPropertyDerivedType(typeof(Redaction), typeDiscriminator: "m.room.redaction")]
[JsonPropertyDerivedType(typeof(RoomMember), typeDiscriminator: "m.room.member")]
[JsonPropertyDerivedType(typeof(CanonicalAlias), typeDiscriminator: "m.room.canonical_alias")]
[JsonPropertyDerivedType(typeof(RoomCreation), typeDiscriminator: "m.room.create")]
[JsonPropertyDerivedType(typeof(JoinRules), typeDiscriminator: "m.room.join_rules")]
public record Event([JsonPropertyTargetProperty] EventContent? content, string type, string? state_key, string? sender, EventID? event_id) {
	public bool IsState { get { return state_key is not null; } }
};

/// <summary>Represents a room event content</summary>
public abstract record EventContent() { };
public record UnknownEventContent() : EventContent() { };

/// <summary> Represents any <c>m.room.message</c> event. </summary>
[JsonNonFirstPolymorphic(TypeDiscriminatorPropertyName = "msgtype", DefaultType = typeof(UnknownMessage))]
[JsonNonFirstDerivedType(typeof(TextMessage), typeDiscriminator: "m.text")]
[JsonNonFirstDerivedType(typeof(ImageMessage), typeDiscriminator: "m.image")]
public abstract record Message(string body, string msgtype) : EventContent();
/// <summary> Represents a message with an unknown type. </summary>
public record UnknownMessage(string body, string msgtype) : Message(body, msgtype);
/// <summary> Represents a basic <c>msgtype: m.text</c> message. </summary>
public record TextMessage(string body) : Message(body, "m.text");
/// <summary> Represents an image in a <c>msgtype: m.image</c> message. </summary>
public record ImageMessage(
	string body,
	// EncryptedFile file, // NOT IMPLEMENTED: E2EE
	string? filename,
	ImageInfo info,
	MXC url
) : Message(body, "m.image");

public record ImageInfo(
	int h,
	string mimetype,
	int size,
	int w,
	// EncryptedFile thumbnail_file // NOT IMPLEMENTED: E2EE
	ThumbnailInfo? thumbnail_info,
	MXC? thumbnail_url
) : ThumbnailInfo(h, mimetype, size, w);

public record ThumbnailInfo(
	int h,
	string mimetype,
	int size,
	int w
);


public record struct MXC(
	string server_name,
	string media_id
) {
	public override string ToString() {
		return $"mxc://{server_name}/{media_id}";
	}
	public MXC(string s) : this("", "") {
		if (!s.StartsWith("mxc://")) throw new FormatException("Could not convert to MXC: doesn't start with mxc://");
		s = s.Substring(6);
		string[] parts = s.Split("/");
		if (parts.Count() != 2) throw new FormatException("Could not convert to MXC: invalid url format");
		server_name = parts[0];
		media_id = parts[1];
	}
};
