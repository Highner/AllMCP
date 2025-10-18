using AllMCPSolution.Repositories;

namespace AllMCPSolution.Tools;

public abstract class UserToolBase : CrudToolBase
{
    protected UserToolBase(IUserRepository userRepository)
    {
        UserRepository = userRepository;
    }

    protected IUserRepository UserRepository { get; }
}
