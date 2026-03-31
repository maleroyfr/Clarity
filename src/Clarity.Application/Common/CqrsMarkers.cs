using MediatR;

namespace Clarity.Application.Common;

/// <summary>Marker for commands that return a result.</summary>
public interface ICommand<out TResult> : IRequest<TResult> { }

/// <summary>Marker for commands that return nothing.</summary>
public interface ICommand : IRequest { }

/// <summary>Marker for queries.</summary>
public interface IQuery<out TResult> : IRequest<TResult> { }

public interface ICommandHandler<in TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult> { }

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand { }

public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult> { }
