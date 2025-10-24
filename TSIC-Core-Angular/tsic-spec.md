# TSIC-CORE-ANGULAR SPECS    
## Target Audience
### Parents/Players
Parents register their children to participate in youth sports clubs, camps, and clinics.
### Club Reps
Club Reps register teams to play in tournaments
### Job Administrators
Job Administrators have full control over their events.  Typical activities include:
- searching
- emailing
- maintaining site bulletins
- moving players between teams
- moving teams between pools
### Other
Other registrants include:
- referees
- college recruiters
- team staff (coaches, assistants etc)
### User Access Use Cases:
- General public visiting TSIC main page looking for infor about TSIC (TeamSportsInfo.com)
- General public visiting a specific job site for details about that event
- A logged in player accessing their logged in registration site for details and to communicate.
- A logged in club rep accessing their logged in registration to view details about their teams and to communicate.
-- a logged in Job Administrator looking to administer their logged in registration site.
## Multi-Tenancy
Every registration is specific to a Job.  A job is a distinct entry in the Jobs database table. 
## Security
A user of any kind gains access to jobs they are registered for by logging in and selecting an active registration.  The login process
- authenticates their credentials then passess back to the UI the list of prior registrations available to the user
- the UI then displays this list from which they select one role
- the backend then authorizes that registration and returns a JWT token (and a refresh token) that contains that registrations "regId" (registrationId from the Jobs.Registrations table)

The JWT token with its regId is the key for the server api to know the registrants target (from foreign key relations stemming from the Jobs.Registration table):
- job name
- role
- username
- user data (first name, last name, email, phone, etc)
- when registrant is a player can also know details of the family they are part of (mom, dad, other kids in family and all their data)

The availability of information to a logged in user is controlled first by the Job they are in.  A user can never access priviledged information from a Job other than the one they are logged in for.  Secondly, information release and display is controlled by the role they are logged in under.

Additionally a user has no privileged access with only an authenticated username and password.  They must also have a JWT token with a regId

### Routing/Job Urls
Although it would easier and most logical to use the regId in the url, clients prefer to see their job names in the url.  Consequently Job Urls are composed of a TSIC root + jobUrl (which is obtained from the Jobs.Jobs table in field "jobPath")
## Dotnet Core Api Backend
## Angular Frontend
### UI requirements
- modern clean state-of-the art views mostly geared to adults (although children are registered, its their parents doing the registration)
- fully responsive and will be access via computer, tablet, and phone