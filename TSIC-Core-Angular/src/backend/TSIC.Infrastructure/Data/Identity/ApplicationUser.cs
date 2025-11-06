using System;
using Microsoft.AspNetCore.Identity;
using TSIC.Domain.Constants;

namespace TSIC.Infrastructure.Data.Identity
{
    // Custom Identity user mapped to existing AspNetUsers table with extra columns
    public class ApplicationUser : IdentityUser
    {
        // Profile fields not present on IdentityUser
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Gender { get; set; }

        // Legacy/contact fields
        public string? Cellphone { get; set; }
        public string? Phone { get; set; }

        // Address fields
        public string? StreetAddress { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }

        // Birth date (DB column "dob")
        public DateTime? Dob { get; set; }

        // Multi-tenant auditing/ownership
        public string LebUserId { get; set; } = TsicConstants.SuperUserId;

        // Modified timestamp (default to UTC now in app layer)
        public DateTime Modified { get; set; } = DateTime.UtcNow;
    }
}
