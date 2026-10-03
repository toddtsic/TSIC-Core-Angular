/**
 * Username characters — mirrors IdentityOptions.User.AllowedUserNameCharacters in
 * TSIC.API/Program.cs. Keep the two identical: a stricter copy here rejects usernames
 * the server accepts (tens of thousands of existing accounts contain '@' or a space).
 */
export const USERNAME_PATTERN = /^[A-Za-z0-9\-!._@+\/ ]+$/;
