# IIS Setup Guide for TSIC Application
# Windows Server 10.0.0.45

## Prerequisites
- IIS installed with following features:
  - Web Server (IIS)
  - Web Server > Application Development > ASP.NET 4.8
  - Web Server > Application Development > .NET Extensibility 4.8
  - Web Server > Application Development > ISAPI Extensions
  - Web Server > Application Development > ISAPI Filters
  - Web Server > Security > Windows Authentication
  - Web Server > Application Development > .NET Core Hosting Bundle (for .NET 9)

## Step 1: Create Application Pools

### TSIC-API-Pool (for .NET API)
1. Open IIS Manager
2. Click on "Application Pools"
3. Right-click > "Add Application Pool"
4. Name: `TSIC-API-Pool`
5. .NET CLR Version: `No Managed Code` (for .NET Core/9)
6. Process Model > Identity: `ApplicationPoolIdentity`
7. Click OK

### TSIC-Angular-Pool (for Angular SPA)
1. Right-click > "Add Application Pool"
2. Name: `TSIC-Angular-Pool`
3. .NET CLR Version: `No Managed Code`
4. Process Model > Identity: `ApplicationPoolIdentity`
5. Click OK

## Step 2: Create Websites

### TSIC-API-CP Website (Port 5000)
1. Right-click on "Sites" > "Add Website"
2. Site name: `TSIC-API-CP`
3. Application pool: `TSIC-API-Pool`
4. Physical path: `D:\Websites\TSIC-API-CP` (or `E:\Websites\TSIC-API-CP` for production)
5. Binding:
   - Type: `http`
   - IP address: `10.0.0.45`
   - Port: `5000`
   - Host name: (leave blank)
6. Click OK

### TSIC-Angular-CP Website (Port 80)
1. Right-click on "Sites" > "Add Website"
2. Site name: `TSIC-Angular-CP`
3. Application pool: `TSIC-Angular-Pool`
4. Physical path: `D:\Websites\TSIC-Angular-CP` (or `E:\Websites\TSIC-Angular-CP` for production)
5. Binding:
   - Type: `http`
   - IP address: `10.0.0.45`
   - Port: `80`
   - Host name: (leave blank)
6. Click OK

## Step 3: Configure Authentication

### For TSIC-API-CP:
1. Select TSIC-API-CP site
2. Double-click "Authentication"
3. Enable: `Anonymous Authentication`
4. Enable: `Windows Authentication` (if needed for your API)
5. Disable: `Forms Authentication`

### For TSIC-Angular-CP:
1. Select TSIC-Angular-CP site
2. Double-click "Authentication"
3. Enable: `Anonymous Authentication`
4. Disable others

## Step 4: Configure Default Documents

### For TSIC-Angular-CP:
1. Select TSIC-Angular-CP site
2. Double-click "Default Document"
3. Add `index.html` to the list (move to top if not already)

## Step 5: Configure MIME Types (for Angular)

### For TSIC-Angular-CP:
1. Select TSIC-Angular-CP site
2. Double-click "MIME Types"
3. Add these if missing:
   - `.json` → `application/json`
   - `.woff` → `application/font-woff`
   - `.woff2` → `font/woff2`

## Step 6: Configure URL Rewrite (for Angular SPA routing)

### For TSIC-Angular-CP:
1. Install URL Rewrite module if not installed:
   - Download from: https://www.iis.net/downloads/microsoft/url-rewrite
2. Select TSIC-Angular-CP site
3. Double-click "URL Rewrite"
4. Right-click "Inbound Rules" > "Add Rule" > "Blank Rule"
5. Name: `Angular SPA Routing`
6. Pattern: `.*`
7. Conditions:
   - Add Condition
   - Input: `{REQUEST_FILENAME}`
   - Check if input string: `Does Not Match the Pattern`
   - Pattern: `.*\.(css|js|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$`
8. Action:
   - Action type: `Rewrite`
   - Rewrite URL: `/index.html`
9. Click Apply

## Step 7: Configure CORS (Cross-Origin Resource Sharing)

### For TSIC-API (if needed):
1. In your API's `Program.cs` or `Startup.cs`, add:
```csharp
services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", builder =>
    {
        builder.WithOrigins("http://10.0.0.45")
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

app.UseCors("AllowAngular");
```

## Step 8: Test Configuration

1. Start both websites in IIS Manager
2. Test API: http://10.0.0.45:5000
3. Test Angular: http://10.0.0.45
4. Check Windows Event Viewer for any errors

## Step 9: Firewall Configuration

Ensure these ports are open in Windows Firewall:
- Port 80 (HTTP for Angular)
- Port 5000 (HTTP for API)

## Troubleshooting

### Common Issues:
1. **Port conflicts**: Check if other services are using ports 80/5000
2. **Permissions**: Ensure ApplicationPoolIdentity has read access to website directories
3. **.NET Core hosting**: Verify .NET Core Hosting Bundle is installed
4. **web.config**: Check for proper configuration in both web.config files

### Logs to check:
- IIS logs: `C:\inetpub\logs\LogFiles\`
- Event Viewer: Windows Logs > System/Application
- Application logs: Check your API's logging configuration

## Security Notes
- Consider using HTTPS in production
- Configure proper authentication/authorization
- Regular security updates for Windows Server
- Monitor logs for suspicious activity