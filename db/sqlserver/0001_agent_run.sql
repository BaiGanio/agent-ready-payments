-- Lab 09: the agent loop's notepad.
--
-- EF Core owns these two tables, but they still ship as a DbUp script, so the schema stays a
-- file you can read and diff, the same as every other table (db/README.md).
--
-- SQL Server only. The API never reads AgentRun or AgentStep, so the lockstep rule does not
-- ask for a Postgres twin.
--
-- Every rule the loop depends on is a constraint here too. A bug in C# has to hit a wall,
-- not a column that takes any string you give it.

create table dbo.AgentRun (
    Id            uniqueidentifier not null
        constraint PK_AgentRun primary key,

    -- Which configured agent ran: support-bot, read-bot.
    AgentName     varchar(50)      not null,

    -- The model and the prompt that produced this run. Nullable ModelId: the run row is
    -- written before the first model reply comes back with its id.
    ModelId       varchar(200)     null,
    PromptVersion varchar(20)      not null,

    Status        varchar(20)      not null
        constraint CK_AgentRun_Status
            check (Status in ('Running', 'WaitingForHuman', 'Done', 'Failed')),

    -- Tool-calling passes so far. The loop's step cap reads this.
    StepCount     int              not null
        constraint DF_AgentRun_StepCount default 0,

    -- SQL Server 2022 has no json type. nvarchar(max) plus an ISJSON check is what makes a
    -- column JSON here: a half-written history is refused with error 547 instead of being
    -- stored and crashing the next resume.
    Messages      nvarchar(max)    not null
        constraint CK_AgentRun_Messages_IsJson check (isjson(Messages) = 1),

    -- The refund the agent proposed and a human has not answered yet. Null unless the run
    -- is WaitingForHuman.
    PendingCall   nvarchar(max)    null
        constraint CK_AgentRun_PendingCall_IsJson
            check (PendingCall is null or isjson(PendingCall) = 1),

    -- The final answer, when Done.
    Answer        nvarchar(max)    null,

    -- Why it Failed: tool_failure_budget, step_cap, ...
    Reason        varchar(50)      null,

    CreatedUtc    datetimeoffset(6) not null
        constraint DF_AgentRun_CreatedUtc default sysdatetimeoffset(),
    UpdatedUtc    datetimeoffset(6) not null
        constraint DF_AgentRun_UpdatedUtc default sysdatetimeoffset(),

    -- The database's own counter, not ours. EF Core's concurrency token is worth nothing
    -- unless the column moves by itself on every update.
    RowVer        rowversion       not null
);
GO

create table dbo.AgentStep (
    Id            uniqueidentifier not null
        constraint PK_AgentStep primary key,

    RunId         uniqueidentifier not null
        constraint FK_AgentStep_AgentRun references dbo.AgentRun (Id),

    -- 1, 2, 3 ... within one run. The order the steps happened in.
    Ordinal       int              not null,

    Kind          varchar(20)      not null
        constraint DF_AgentStep_Kind default 'tool'
        constraint CK_AgentStep_Kind check (Kind in ('tool', 'approval', 'guard')),

    -- Null for a guard step: no tool was called.
    ToolName      varchar(100)     null,
    InputJson     nvarchar(max)    null
        constraint CK_AgentStep_InputJson_IsJson
            check (InputJson is null or isjson(InputJson) = 1),
    OutputJson    nvarchar(max)    null
        constraint CK_AgentStep_OutputJson_IsJson
            check (OutputJson is null or isjson(OutputJson) = 1),

    Outcome       varchar(20)      not null
        constraint CK_AgentStep_Outcome check (Outcome in ('Ok', 'ToolError', 'Rejected')),

    ModelId       varchar(200)     null,
    PromptVersion varchar(20)      null,

    -- Who said yes or no, on an approval step.
    Approver      nvarchar(100)    null,

    DurationMs    int              not null
        constraint DF_AgentStep_DurationMs default 0,
    CreatedUtc    datetimeoffset(6) not null
        constraint DF_AgentStep_CreatedUtc default sysdatetimeoffset(),

    -- Two workers resuming the same run would both write step 1. This is what stops a run
    -- having two step 1s and losing one of them.
    constraint UX_AgentStep_Run_Ordinal unique (RunId, Ordinal)
);
GO

-- The loop loads a run's steps in order, every pass.
create index IX_AgentStep_RunId_Ordinal on dbo.AgentStep (RunId, Ordinal);
GO
