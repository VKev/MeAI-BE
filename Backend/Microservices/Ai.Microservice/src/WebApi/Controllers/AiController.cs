using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Common;

namespace WebApi.Controllers
{
    [Route("api/[controller]")]
    public class AiController(IMediator mediator) : ApiController(mediator)
    {
        
    }
}
