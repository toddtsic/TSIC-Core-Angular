using System.Collections.Generic;
using TSIC.Application.DTOs;

namespace TSIC.API.Dtos
{
    public record LoginResponseDto
    {
        public required string UserId { get; init; }
        public required List<RegistrationRoleDto> Registrations { get; init; }
    }
}
