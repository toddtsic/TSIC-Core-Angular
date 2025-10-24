using System.Collections.Generic;
using TSIC.Application.DTOs;

namespace TSIC.API.Dtos
{
    public record LoginResponseDto(List<RegistrationRoleDto> Registrations);
}
