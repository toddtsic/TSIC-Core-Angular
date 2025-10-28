# SSL Certificate Setup for Angular Development

## Quick Start

Run the setup script to generate and trust a local SSL certificate:

```powershell
cd scripts
.\setup-ssl-cert.ps1
```

This will:
1. Generate a self-signed certificate for `localhost`
2. Export it to the `TSIC-Core-Angular/src/frontend/tsic-app/ssl/` directory
3. Trust the certificate in your Windows certificate store
4. Create the necessary files for Angular dev server

## What Gets Created

- `ssl/localhost.pfx` - Certificate with private key (password: "angular")
- `ssl/localhost.crt` - Public certificate
- `ssl/localhost.pem` - PEM format certificate

## After Running the Script

1. **Restart your browser** (close all windows)
2. **Restart the Angular dev server** if it's running:
   ```powershell
   # Stop current server (Ctrl+C), then:
   npm start
   ```
3. Navigate to `https://localhost:4300`
4. You should see a secure connection (🔒) without certificate warnings

## Troubleshooting

### Still seeing certificate errors?

1. **Close ALL browser windows** completely
2. **Clear browser cache** (Ctrl+Shift+Delete)
3. Try accessing `https://localhost:4300` again

### Certificate expired?

The certificate is valid for 2 years. When it expires, just run the setup script again:
```powershell
.\setup-ssl-cert.ps1
```

### Need to remove the certificate?

Open Windows Certificate Manager:
```powershell
certmgr.msc
```
Navigate to: `Trusted Root Certification Authorities > Certificates`  
Find "localhost" and delete it.

## Why SSL for Development?

- Matches production environment
- Required for some browser APIs (PWA, Service Workers, etc.)
- Enables testing of secure features
- Prevents mixed content warnings when calling HTTPS APIs

## Security Note

⚠️ These certificates are for **local development only**.  
They should NEVER be used in production or committed to source control.
