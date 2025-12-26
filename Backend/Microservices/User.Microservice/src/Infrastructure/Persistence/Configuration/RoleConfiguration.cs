using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        var createdAt = new DateTime(2025, 01, 01, 0, 0, 0, DateTimeKind.Utc);

        builder.HasData(
            new Role
            {
                Id = new Guid("6e01f859-0a6d-4cc8-a0f0-4b0f46a7cf01"),
                Name = "ADMIN",
                Description = "Administrator",
                CreatedAt = createdAt
            },
            new Role
            {
                Id = new Guid("7f02d7b4-8b14-4a9d-86a2-ff2b1bc7f902"),
                Name = "USER",
                Description = "Standard user",
                CreatedAt = createdAt
            },
            new Role
            {
                Id = new Guid("8a8c0fe8-2f0f-4f77-9b1a-d1fbf6a4b403"),
                Name = "MODERATOR",
                Description = "Moderator",
                CreatedAt = createdAt
            },
            new Role
            {
                Id = new Guid("90c2bf14-6ad9-4e0d-81e9-5225d531e104"),
                Name = "BANNED",
                Description = "Banned user",
                CreatedAt = createdAt
            });
    }
}
