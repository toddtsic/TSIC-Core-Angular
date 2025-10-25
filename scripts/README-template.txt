TSIC Deployment Package - Current
====================================

This package contains the latest build of TSIC .NET API and Angular frontend.

iDRIVE DEPLOYMENT WORKFLOW:
===========================

1. This folder has been backed up to iDrive from your development machine
2. RDP to server 10.0.0.45 through VPN
3. Restore this ENTIRE folder from iDrive to: [Drive]:\Websites\tsic-deployment-current
4. On the server, open PowerShell as Administrator and run:
   cd "[Drive]:\Websites\tsic-deployment-current"
   .\deploy-to-server.ps1

WHAT HAPPENS ON THE SERVER:
- API files copied to [Drive]:\Websites\TSIC-API-CP\
- Angular files copied to [Drive]:\Websites\TSIC-Angular-CP\
- IIS websites TSIC-API-CP and TSIC-Angular-CP are automatically stopped/started during deployment
- IIS web.config files configured
- IIS application pools restarted when available

SECURITY NOTES:
- No additional firewall ports opened
- Uses your existing RDP VPN access
- Leverages iDrive's secure backup infrastructure
- No admin shares or remote file access required
- IIS websites are automatically stopped/started during deployment

IIS SETUP REQUIREMENTS:
- See docs/IIS-Setup-Guide.md for complete IIS configuration instructions
- Ensure both websites and application pools are created before first deployment

PACKAGE CONTENTS:
api\           - .NET API files + web.config
angular\       - Angular frontend files + web.config
deploy-to-server.ps1 - Server deployment script
README.txt     - This file

CREATED: {timestamp}
BACKUP LOCATION: iDrive backup set
RESTORE LOCATION: [Drive]:\Websites\tsic-deployment-current (on server)