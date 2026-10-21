create table idempotency_key (
    merchant_id       uuid        not null references merchant (id),
    key               text        not null check (length(key) between 1 and 255),
    request_hash      char(64)    not null,
    status            text        not null check (status in ('InProgress', 'Completed')),
    response_code     int         null,
    response_body     text        null,
    response_location text        null,
    created_at        timestamptz not null default now(),
    completed_at      timestamptz null,

    -- Two inserts of the same pair cannot both succeed, however close together they arrive.
    -- This is the whole safety of the lab. No "if" in C# can promise it.
    primary key (merchant_id, key),

    -- "Completed with no answer" is a state the database refuses, so a bug in the store
    -- shows up as an error instead of as an empty replay.
    constraint idempotency_key_completed_has_an_answer check (
        status = 'InProgress'
        or (response_code is not null
            and response_body is not null
            and completed_at is not null)
    )
);
