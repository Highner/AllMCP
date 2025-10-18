using AllMCPSolution.Repositories;

namespace AllMCPSolution.Tools;

public abstract class TastingNoteToolBase : CrudToolBase
{
    protected TastingNoteToolBase(
        ITastingNoteRepository tastingNoteRepository,
        IBottleRepository bottleRepository,
        IUserRepository userRepository)
    {
        TastingNoteRepository = tastingNoteRepository;
        BottleRepository = bottleRepository;
        UserRepository = userRepository;
    }

    protected ITastingNoteRepository TastingNoteRepository { get; }
    protected IBottleRepository BottleRepository { get; }
    protected IUserRepository UserRepository { get; }
}
