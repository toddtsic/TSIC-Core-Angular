# Phase 6 — Team Chat + SignalR Migration Script

> **Applies to**: TSIC-Teams Ionic app
> **Backend**: 1 REST endpoint + SignalR hub at `/hubs/chat`

---

## 1. Get Chat Messages (REST)

```
OLD: POST api/tsic_teams_2025/Chat/GetTeamMessages
NEW: POST api/teams/{teamId}/chat
```

```typescript
// Request
interface GetChatMessagesRequest {
  teamId: string;
  pageNumber: number;    // 1-based
  rowsPerPage: number;   // default 50
}

// Response
interface GetChatMessagesResponse {
  messages: ChatMessageDto[];
  includesAll: boolean;    // true if no more pages
}

interface ChatMessageDto {
  messageId: string;
  message: string;
  teamId: string;
  creatorUserId: string;
  created: string;       // ISO DateTime
  createdBy?: string;    // "FirstName LastName"
  myMessage: boolean;    // true if current user sent this
}
```

---

## 2. SignalR Hub

```
OLD: /ChatHub
NEW: /hubs/chat
```

### Connection Setup (Ionic)

```typescript
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr';

const connection: HubConnection = new HubConnectionBuilder()
  .withUrl(`${baseUrl}/hubs/chat`, {
    accessTokenFactory: () => getAccessToken()
  })
  .withAutomaticReconnect()
  .build();

await connection.start();
```

### Hub Methods (Client → Server)

```typescript
// Join a team chat room
await connection.invoke('JoinGroup', teamId);

// Leave a team chat room
await connection.invoke('LeaveGroup', teamId);

// Send a message
await connection.invoke('AddTeamChatMessage', teamId, userId, messageText);

// Delete a message
await connection.invoke('DeleteTeamChatMessage', teamId, messageId);
```

### Hub Events (Server → Client)

```typescript
// Listen for new messages
connection.on(`newmessage_${teamId}`, (message: ChatMessageDto) => {
  // Add to local message list
});

// Listen for deleted messages
connection.on(`deletemessage_${teamId}`, (messageId: string) => {
  // Remove from local message list
});
```

### Changes from Legacy

- Hub URL: `/ChatHub` → `/hubs/chat`
- Authentication: JWT token via `accessTokenFactory` (was cookie-based)
- Method signatures unchanged: `JoinGroup`, `LeaveGroup`, `AddTeamChatMessage`, `DeleteTeamChatMessage`
- Event names unchanged: `newmessage_{teamId}`, `deletemessage_{teamId}`

### Ionic Package

```bash
npm install @microsoft/signalr
```
