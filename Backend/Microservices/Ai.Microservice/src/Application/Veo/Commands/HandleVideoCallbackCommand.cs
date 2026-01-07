using Application.Veo.Models;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;

namespace Application.Veo.Commands;

public sealed record HandleVideoCallbackCommand(VeoCallbackPayload Payload) : IRequest<Result<bool>>;
