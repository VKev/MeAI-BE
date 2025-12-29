using Application.Users.Contracts;
using SharedLibrary.Abstractions.Messaging;

namespace Application.Users.Queries.GetUsers;

public sealed record GetUsersQuery(bool IncludeDeleted) : IQuery<IReadOnlyList<AdminUserResponse>>;
