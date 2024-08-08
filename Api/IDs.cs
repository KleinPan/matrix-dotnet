namespace matrix_dotnet.Api;

public record struct EventID(string id) {
	public override string ToString() {
		return id;
	}
}

public record struct RoomID(string id) {
	public override string ToString() {
		return id;
	}
}
