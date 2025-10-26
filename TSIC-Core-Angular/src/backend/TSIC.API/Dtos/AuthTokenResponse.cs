namespace TSIC.API.Dtos
{
    public record AuthTokenResponse(
        string AccessToken,
        int ExpiresIn,
        AuthenticatedUserDto User
    );

    public record AuthenticatedUserDto(
        string UserId,
        string Username,
        string FirstName,
        string LastName,
        string SelectedRole,
        string JobPath
    );
}
