create table cdn_bucket_items
(
    id        text                      not null
        primary key,
    hash      text                      not null,
    mime_type text                      not null,
    user_id   bigint                    not null,
    category  integer                   not null,
    file_name text default 'file'::text not null
);
grant delete, insert, references, select, trigger, truncate, update on cdn_bucket_items to "valour-user";

create table cdn_proxies
(
    id        text not null
        primary key,
    origin    text not null,
    mime_type text not null,
    width     integer,
    height    integer
);

grant delete, insert, references, select, trigger, truncate, update on cdn_proxies to "valour-user";

