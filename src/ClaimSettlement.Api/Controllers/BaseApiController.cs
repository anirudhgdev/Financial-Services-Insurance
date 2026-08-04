using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimSettlement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public abstract class BaseApiController : ControllerBase
{
}
