using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Config",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatModel = table.Column<string>(type: "text", nullable: true),
                    MediaAspectRatio = table.Column<string>(type: "text", nullable: true),
                    NumberOfVariances = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Config", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Role",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    NumberOfSocialAccounts = table.Column<int>(type: "integer", nullable: true),
                    MeAiCoin = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    RateLimitForContentCreation = table.Column<int>(type: "integer", nullable: true),
                    NumberOfWorkspaces = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscription", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: true),
                    Birthday = table.Column<DateTime>(type: "date", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    AvatarResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    MeAiCoin = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplateContent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: false),
                    TextBody = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplateContent", x => x.Id);
                    table.ForeignKey(
                        name: "email_template_contents_email_template_id_fkey",
                        column: x => x.EmailTemplateId,
                        principalTable: "EmailTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshToken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    AccessTokenJti = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    AccessTokenRevokedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshToken", x => x.Id);
                    table.ForeignKey(
                        name: "refresh_tokens_user_id_fkey",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Resource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Link = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true),
                    ResourceType = table.Column<string>(type: "text", nullable: true),
                    ContentType = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resource", x => x.Id);
                    table.ForeignKey(
                        name: "resources_user_id_fkey",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SocialMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialMedia", x => x.Id);
                    table.ForeignKey(
                        name: "social_media_user_id_fkey",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationType = table.Column<string>(type: "text", nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TransactionType = table.Column<string>(type: "text", nullable: true),
                    TokenUsed = table.Column<int>(type: "integer", nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transaction", x => x.Id);
                    table.ForeignKey(
                        name: "transactions_user_id_fkey",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRole",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRole", x => x.Id);
                    table.ForeignKey(
                        name: "user_roles_role_id_fkey",
                        column: x => x.RoleId,
                        principalTable: "Role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "user_roles_user_id_fkey",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveDate = table.Column<DateTime>(type: "timestamp", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscription", x => x.Id);
                    table.ForeignKey(
                        name: "user_subscriptions_subscription_id_fkey",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "user_subscriptions_user_id_fkey",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Workspace",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspace", x => x.Id);
                    table.ForeignKey(
                        name: "workspaces_user_id_fkey",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceSocialMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialMediaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceSocialMedia", x => x.Id);
                    table.ForeignKey(
                        name: "workspace_social_media_social_media_id_fkey",
                        column: x => x.SocialMediaId,
                        principalTable: "SocialMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "workspace_social_media_user_id_fkey",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "workspace_social_media_workspace_id_fkey",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Role",
                columns: new[] { "Id", "CreatedAt", "DeletedAt", "Description", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("6e01f859-0a6d-4cc8-a0f0-4b0f46a7cf01"), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Administrator", "ADMIN", null },
                    { new Guid("7f02d7b4-8b14-4a9d-86a2-ff2b1bc7f902"), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Standard user", "USER", null },
                    { new Guid("8a8c0fe8-2f0f-4f77-9b1a-d1fbf6a4b403"), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Moderator", "MODERATOR", null },
                    { new Guid("90c2bf14-6ad9-4e0d-81e9-5225d531e104"), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Banned user", "BANNED", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplate_Key",
                table: "EmailTemplate",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplateContent_EmailTemplateId_Language",
                table: "EmailTemplateContent",
                columns: new[] { "EmailTemplateId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_AccessTokenJti",
                table: "RefreshToken",
                column: "AccessTokenJti",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TokenHash",
                table: "RefreshToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_UserId",
                table: "RefreshToken",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Resource_UserId",
                table: "Resource",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Role_Name",
                table: "Role",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialMedia_Metadata",
                table: "SocialMedia",
                column: "Metadata")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMedia_UserId",
                table: "SocialMedia",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_RelationId",
                table: "Transaction",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_UserId",
                table: "Transaction",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_User_Email",
                table: "User",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_Username",
                table: "User",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_RoleId",
                table: "UserRole",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_UserId",
                table: "UserRole",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscription_SubscriptionId",
                table: "UserSubscription",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscription_UserId",
                table: "UserSubscription",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Workspace_UserId",
                table: "Workspace",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSocialMedia_SocialMediaId",
                table: "WorkspaceSocialMedia",
                column: "SocialMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSocialMedia_UserId",
                table: "WorkspaceSocialMedia",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSocialMedia_WorkspaceId",
                table: "WorkspaceSocialMedia",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSocialMedia_WorkspaceId_SocialMediaId",
                table: "WorkspaceSocialMedia",
                columns: new[] { "WorkspaceId", "SocialMediaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Config");

            migrationBuilder.DropTable(
                name: "EmailTemplateContent");

            migrationBuilder.DropTable(
                name: "RefreshToken");

            migrationBuilder.DropTable(
                name: "Resource");

            migrationBuilder.DropTable(
                name: "Transaction");

            migrationBuilder.DropTable(
                name: "UserRole");

            migrationBuilder.DropTable(
                name: "UserSubscription");

            migrationBuilder.DropTable(
                name: "WorkspaceSocialMedia");

            migrationBuilder.DropTable(
                name: "EmailTemplate");

            migrationBuilder.DropTable(
                name: "Role");

            migrationBuilder.DropTable(
                name: "Subscription");

            migrationBuilder.DropTable(
                name: "SocialMedia");

            migrationBuilder.DropTable(
                name: "Workspace");

            migrationBuilder.DropTable(
                name: "User");
        }
    }
}
