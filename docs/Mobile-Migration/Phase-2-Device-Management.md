# Phase 2 — Device Management Migration Script

> **Applies to**: Both TSIC-Events and TSIC-Teams Ionic apps
> **Backend endpoints**: 4 new endpoints on `DeviceController`

---

## 1. Register Device for Job

**URL Change:**
```
OLD (Events): POST api/tsic_events_2025/Firebase/JobDevice_Add
OLD (Teams):  (implicit — device registered during login)
NEW:          POST api/devices/register
```

**Request Interface:**
```typescript
// OLD
interface Firebase_SetDeviceJob_RequestModel {
  DeviceToken: string;
  JobId: string;       // Guid
  DeviceType: string;  // "ios" | "android"
}

// NEW
interface RegisterDeviceRequest {
  deviceToken: string;    // camelCase
  jobId: string;          // Guid
  deviceType: string;     // "ios" | "android"
}
```

**Service Method:**
```typescript
// OLD
registerDevice(token: string, jobId: string, type: string): Observable<void> {
  return this.http.post<void>(
    `${this.baseUrl}/api/tsic_events_2025/Firebase/JobDevice_Add`,
    { DeviceToken: token, JobId: jobId, DeviceType: type }
  );
}

// NEW
registerDevice(token: string, jobId: string, type: string): Observable<void> {
  return this.http.post<void>(
    `${this.baseUrl}/api/devices/register`,
    { deviceToken: token, jobId, deviceType: type }
  );
}
```

---

## 2. Toggle Team Subscription (Favorite)

**URL Change:**
```
OLD: POST api/tsic_events_2025/Firebase/ToggleDevice_TeamSubscription
NEW: POST api/devices/subscribe-team?jobId={jobId}
```

**Request Interface:**
```typescript
// OLD
interface Firebase_ToggleDevice_TeamSubscription_RequestModel {
  DeviceToken: string;
  TeamId: string;        // Guid
  DeviceType: string;    // "ios" | "android"
}

// NEW
interface ToggleTeamSubscriptionRequest {
  deviceToken: string;
  teamId: string;
  deviceType: string;
}
```

**Response Interface:**
```typescript
// OLD — returned List<TeamSearchParamOption> (full team objects with subscription status)

// NEW — returns just the updated subscribed team IDs
interface ToggleTeamSubscriptionResponse {
  subscribedTeamIds: string[];   // Guid[]
}
```

**Service Method:**
```typescript
// OLD
toggleTeamSubscription(token: string, teamId: string, type: string): Observable<TeamSearchParamOption[]> {
  return this.http.post<TeamSearchParamOption[]>(
    `${this.baseUrl}/api/tsic_events_2025/Firebase/ToggleDevice_TeamSubscription`,
    { DeviceToken: token, TeamId: teamId, DeviceType: type }
  );
}

// NEW
toggleTeamSubscription(token: string, teamId: string, type: string, jobId: string): Observable<ToggleTeamSubscriptionResponse> {
  return this.http.post<ToggleTeamSubscriptionResponse>(
    `${this.baseUrl}/api/devices/subscribe-team?jobId=${jobId}`,
    { deviceToken: token, teamId, deviceType: type }
  );
}
```

**Migration Note:** The new response returns `subscribedTeamIds` (array of Guid strings) instead of full team objects. Update your local state by checking `subscribedTeamIds.includes(teamId)` instead of mapping from `TeamSearchParamOption.IsFavorited`.

---

## 3. Swap Device Token

**URL Change:**
```
OLD (Events): POST api/tsic_events_2025/Firebase/SwapPhone_DeviceTokens
OLD (Teams):  POST api/tsic_teams_2025/Firebase/SwapPhone_DeviceTokens
NEW:          POST api/devices/swap-token
```

**Request Interface:**
```typescript
// OLD
interface SwapPhone_DeviceTokens_RequestModel {
  OldDeviceToken: string;
  NewDeviceToken: string;
}

// NEW
interface SwapDeviceTokenRequest {
  oldDeviceToken: string;   // camelCase
  newDeviceToken: string;
}
```

**Service Method:**
```typescript
// OLD
swapDeviceToken(oldToken: string, newToken: string): Observable<void> {
  return this.http.post<void>(
    `${this.baseUrl}/api/tsic_events_2025/Firebase/SwapPhone_DeviceTokens`,
    { OldDeviceToken: oldToken, NewDeviceToken: newToken }
  );
}

// NEW
swapDeviceToken(oldToken: string, newToken: string): Observable<void> {
  return this.http.post<void>(
    `${this.baseUrl}/api/devices/swap-token`,
    { oldDeviceToken: oldToken, newDeviceToken: newToken }
  );
}
```

---

## 4. Get Subscriptions (New Endpoint)

**URL:** `GET api/devices/subscriptions/{jobId}?deviceToken={token}`

This endpoint is new — no legacy equivalent. Use it to restore favorite team state when the app opens instead of relying on the search params response.

```typescript
getSubscribedTeams(jobId: string, deviceToken: string): Observable<string[]> {
  return this.http.get<string[]>(
    `${this.baseUrl}/api/devices/subscriptions/${jobId}?deviceToken=${deviceToken}`
  );
}
```

---

## Summary of URL Changes

| # | Old URL | New URL | Both Apps |
|---|---------|---------|-----------|
| 1 | `POST Firebase/JobDevice_Add` | `POST api/devices/register` | Events only (Teams was implicit) |
| 2 | `POST Firebase/ToggleDevice_TeamSubscription` | `POST api/devices/subscribe-team?jobId=` | Events only |
| 3 | `POST Firebase/SwapPhone_DeviceTokens` | `POST api/devices/swap-token` | Both |
| 4 | (none) | `GET api/devices/subscriptions/{jobId}?deviceToken=` | Both (new) |

## Key Differences

1. **All property names are camelCase** in the new API (ASP.NET Core default JSON serialization)
2. **No auth required** for device endpoints — device token is the identity
3. **`jobId` is a query param** on subscribe-team (not in the body) to keep the request body focused
4. **Toggle response** returns team ID list instead of full team objects — simpler, less data
5. **New subscriptions endpoint** replaces the `FavoritedTeams` that was embedded in search params
