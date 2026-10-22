create table api_key (
    key_hash    char(64)    primary key,
    name        text        not null unique,
    merchant_id uuid        not null references merchant (id),
    kind        text        not null check (kind in ('agent', 'service')),
    scopes      text[]      not null,
    created_at  timestamptz not null default now(),
    revoked_at  timestamptz null,

    -- A typo in a scope name is refused here, so a key can never hold a scope no code checks for.
    constraint api_key_known_scopes check (
        scopes <@ array['payments:read', 'payments:write', 'payments:refund', 'payments:approve']
    ),

    -- An agent never approves. The rule lives in the table, so every writer obeys it,
    -- not only the C# code that remembers to check.
    constraint api_key_agent_never_approves check (
        kind <> 'agent' or not ('payments:approve' = any (scopes))
    )
);
