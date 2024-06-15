create procedure batch_user_channel_state_update(user_ids_in bigint[], channel_id_in bigint, new_time timestamp with time zone)
    language plpgsql
as
$$
declare
-- variable declaration
begin
    update user_channel_states
    set last_viewed_time = new_time
    where (channel_id = channel_id_in AND
           user_id = ANY(user_ids_in));
end
$$;

alter procedure batch_user_channel_state_update(bigint[], bigint, timestamp with time zone) owner to postgres;

