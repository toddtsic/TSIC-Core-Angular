using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace TSIC.Infrastructure.Data.Identity
{
    public class TsicIdentityDbContext : IdentityDbContext<ApplicationUser>
    {
        public TsicIdentityDbContext(DbContextOptions<TsicIdentityDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Map custom ApplicationUser properties to existing AspNetUsers column names
            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.FirstName).HasColumnName("FirstName");
                entity.Property(u => u.LastName).HasColumnName("LastName");
                entity.Property(u => u.Gender).HasColumnName("gender");

                entity.Property(u => u.Cellphone).HasColumnName("cellphone");
                entity.Property(u => u.Phone).HasColumnName("phone");

                entity.Property(u => u.StreetAddress).HasColumnName("streetAddress");
                entity.Property(u => u.City).HasColumnName("city");
                entity.Property(u => u.State).HasColumnName("state");
                entity.Property(u => u.PostalCode).HasColumnName("postalCode");
                entity.Property(u => u.Country).HasColumnName("country");

                entity.Property(u => u.Dob).HasColumnName("dob");

                entity.Property(u => u.LebUserId)
                      .HasMaxLength(450)
                      .HasColumnName("lebUserID");

                entity.Property(u => u.Modified)
                      .HasColumnName("modified")
                      .HasDefaultValueSql("(getdate())");
            });
        }
    }
}

