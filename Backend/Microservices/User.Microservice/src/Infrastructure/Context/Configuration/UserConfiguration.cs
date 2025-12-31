using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        entity.HasKey(e => e.Id).HasName("users_pkey");

        entity.ToTable("users");

        entity.HasIndex(e => e.Username, "users_username_key").IsUnique();
        entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Username).HasColumnName("username").IsRequired();
        entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
        entity.Property(e => e.Email).HasColumnName("email").IsRequired();
        entity.Property(e => e.EmailVerified).HasColumnName("email_verified");
        entity.Property(e => e.FullName).HasColumnName("full_name");
        entity.Property(e => e.Birthday).HasColumnName("birthday").HasColumnType("date");
        entity.Property(e => e.PhoneNumber).HasColumnName("phone_number");
        entity.Property(e => e.Provider).HasColumnName("provider");
        entity.Property(e => e.AvatarResourceId).HasColumnName("avatar_resource_id");
        entity.Property(e => e.Address).HasColumnName("address");
        entity.Property(e => e.MeAiCoin)
            .HasColumnName("me_ai_coin")
            .HasColumnType("numeric(18,2)")
            .HasDefaultValue(0m);
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
    }
}
