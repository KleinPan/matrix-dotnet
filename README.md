# matrix-dotnet

`matrix-dotnet` is a ✨modern✨ C# library for the matrix protocol focused on
code brevity. It features a two-layered approach with a Refit-based raw API
interface and a fully featured client class. Modern language features (like the
`goto` keyword) are used to make the code readable and succinct. Also we indent
with tabs.

## Installation

NuGet coming soon™, for the time being we recommend using a git submodule:

```shell
git submodule add https://gitlab.com/Greenscreener/matrix-dotnet
```

and then adding a reference to your `csproj` file:

```shell
dotnet add reference ../matrix-dotnet/matrix-dotnet.csproj
```

## Preface

It is highly recommended to consult relevant parts of the  [Matrix
Specification](https://spec.matrix.org/latest/client-server-api/) when working
with this library as many design decisions follow the specification's rationale.

## Usage

You start by initializing the MatrixClient:

```cs
using matrix_dotnet.Client;

var client = new MatrixClient("https://example.com");
await client.PasswordLogin(Secrets.username, Secrets.password, initialDeviceDisplayName: "FreeReal development");
```

After that, the client will receive an access token and an optional refresh
token. The client **does not** keep your password. Each time the short-lived
access token expires, it will be automatically refreshed using the long-lived
refresh token. The login API is rate limited, so you might want to store the
access and refresh tokens somewhere externally, if you need to run your program
many times.

```cs
using (FileStream stream = File.Create("client.json")) {
	await JsonSerializer.SerializeAsync(stream, client.ToLoginData());
}
```

And afterwards:

```cs
matrix_dotnet.Client.LoginData? loginData = null;
using (FileStream stream = File.OpenRead("client.json")) {
	loginData = await JsonSerializer.DeserializeAsync<matrix_dotnet.Client.LoginData>(stream);
};

MatrixClient? client = new MatrixClient(loginData.Value);
```

Using this technique, you will also be able to keep the same device_id. If you
don't, you **will add a new device to the users logged-in devices** each time you
login. **Logging in with a password each time is discouraged**.

### Receiving messages

First, you should start by calling the `Sync` method:

```cs
await client.Sync();
```

This calls the server's `/sync` endpoint to establish basic information about
all rooms. To get messages for a room, you can just fetch them directly from its
*Timeline*:

```cs
var roomTimeline = client.JoinedRooms[new RoomID("<room_id>")].timeline;
await foreach (var timelineEvent in roomTimeline.First.EnumerateForward()) {
	EventWithState ev = timelineEvent.Value;
	if (ev.Event.content is Message message)
		Console.WriteLine($"{ev.GetSender()?.displayname ?? ev.Event.sender}: {message.body}");
}
```

The *Timeline* is a linked list (you'll see why in a bit). The values are
*Events with state*. In matrix, rooms have state that can change as the room
evolves. This state for instance contains user's display names, which can change
dynamically. The client automatically resolves this state and attaches the
correct state dictionary to each event, so if a user in the example above
changes their displayname mid-conversation, the shown displayname will change
accordingly.

Now, the *Timeline* is **incomplete**, in the beginning, it only contains events
received from the `Sync` call (usually around 10). The `Timeline.First` property
doesn't give you the first *ever* event in the room, but the first currently
cached one. To get earlier events, you want to use `await
Timeline.First.Previous()`. This is an asynchronous method and will
automatically fetch more events when needed, so it can be used to iterate
through the entire history of the room. This is also true for `.Next()` and
`.EnumerateFroward()` and `.EnumerateBackward()`. If this is not desired,
`.NextOffline()` and other `-Offline()` methods are available. In most cases
however, you don't want to use them as **syncing might cause holes in the
timeline**, which the async methods automatically fill in.

To get new messages, you should call the `Sync` method again. If you supply the
optional `timeout` argument, the server will wait for the specified time before
responding. If however a new event arrives, it will respond immediately with the
new event.

The following code can be used to display new messages as they arrive:

```cs
while (true) {
	if ((await lastSeen?.Next()) is not null) {
		await foreach (var timelineEvent in (await lastSeen.Next()).EnumerateForward()) {
			EventWithState ev = timelineEvent.Value;
			if (ev.Event.content is Message message)
				Console.WriteLine($"{ev.GetSender()?.displayname ?? ev.Event.sender}: {message.body}");
			lastSeen = timelineEvent;
		}
	}
	await client.Sync(30000); // Wait for at most 30 seconds or return immediately if new events are received.
}

```

It is **highly recommended** to read the [Syncing section of the Matrix
Specification](https://spec.matrix.org/latest/client-server-api/#syncing) as it
clearly explains most of what was mentioned above.

### Sending messages

Sending messages is much simpler, you can just use the following method:

```cs
var id = await client.SendTextMessage(client.JoinedRooms.Keys.First(), "Hello World!");
```

See in-code documentation for usage of other `MatrixClient` methods.

### Advanced usage

You can call API endpoints directly using the `MatrixClient.ApiClient` property.
This gives you access to the Refit-generated API client.

## Contributing

The library is very incomplete so far, and somewhat on purpose. A lot of
functionality can be added just by adding more event types. If you need anything
not-yet supported, feel free to add it to the `Api/EventContent.cs` file. I'll
be glad to help with any contributions so even unfinished pull requests are very
welcome.

## Known issues, missing features

- event relationships and all related APIs are not implemented
