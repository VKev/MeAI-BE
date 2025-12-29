using Application.Users.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery([Required] Guid UserId) : IQuery<AdminUserResponse>;
