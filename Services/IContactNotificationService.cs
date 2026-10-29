using KarakusAluminyum.Web.Models;
namespace KarakusAluminyum.Web.Services;
public interface IContactNotificationService
{
    Task NotifyAsync(ContactRequest request, CancellationToken cancellationToken = default);
}
