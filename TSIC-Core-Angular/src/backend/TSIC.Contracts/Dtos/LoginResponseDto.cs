using System.Collections.Generic;
using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Dtos
{
    public record LoginResponseDto
    {
        public required string UserId { get; init; }
        public required List<RegistrationRoleDto> Registrations { get; init; }
    }
}
