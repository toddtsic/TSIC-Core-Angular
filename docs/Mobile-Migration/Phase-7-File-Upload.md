# Phase 7 — File Upload Migration Script

> **Applies to**: TSIC-Teams Ionic app
> **Backend**: 2 endpoints on `FileUploadController`

---

## 1. Upload File

```
OLD: POST api/tsic_teams_2025/FileUpload/UploadFile  (multipart form)
NEW: POST api/files/upload  (multipart form)
```

```typescript
// OLD
uploadFile(file: File): Observable<{ fileUrl: string }> {
  const formData = new FormData();
  formData.append('file', file, file.name);
  return this.http.post<{ fileUrl: string }>(
    `${this.baseUrl}/api/tsic_teams_2025/FileUpload/UploadFile`,
    formData
  );
}

// NEW
uploadFile(file: File): Observable<FileUploadResponseDto> {
  const formData = new FormData();
  formData.append('file', file, file.name);
  return this.http.post<FileUploadResponseDto>(
    `${this.baseUrl}/api/files/upload`,
    formData
  );
}

interface FileUploadResponseDto {
  fileUrl: string;    // full URL to uploaded file
}
```

---

## 2. Delete File

```
OLD: POST api/tsic_teams_2025/FileUpload/DeleteFile  (body: { FileUrl })
NEW: POST api/files/delete
```

```typescript
// OLD
deleteFile(fileUrl: string): Observable<void> {
  return this.http.post<void>(
    `${this.baseUrl}/api/tsic_teams_2025/FileUpload/DeleteFile`,
    { FileUrl: fileUrl }
  );
}

// NEW
deleteFile(fileUrl: string): Observable<void> {
  return this.http.post<void>(
    `${this.baseUrl}/api/files/delete`,
    { fileUrl }    // camelCase
  );
}
```

---

## Summary

| # | Old URL | New URL |
|---|---------|---------|
| 1 | `POST FileUpload/UploadFile` | `POST api/files/upload` |
| 2 | `POST FileUpload/DeleteFile` | `POST api/files/delete` |

Both endpoints require `[Authorize]`.
