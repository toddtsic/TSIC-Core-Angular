using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace TSIC.Infrastructure.Data.Identity
{
    public class TsicIdentityDbContext : IdentityDbContext<IdentityUser>
    {
        public TsicIdentityDbContext(DbContextOptions<TsicIdentityDbContext> options)
            : base(options)
        {
        }
    }
}

