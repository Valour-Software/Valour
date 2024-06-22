create function apply_member_access(p_member_id bigint, p_target_id bigint) returns integer
    language plpgsql
as
$$
DECLARE
    v_access_result RECORD;
    v_channel RECORD;
BEGIN
    -- Call check_member_access to get the access result
    v_access_result := check_member_access(p_member_id, p_target_id);

    -- Check if the target exists in channels
    SELECT * INTO v_channel
    FROM channels
    WHERE id = p_target_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Target ID % not found in channels', p_target_id;
    END IF;

    -- If the result is not -1 and even, insert or update the record
    IF v_access_result.value <> -1 AND v_access_result.value % 2 = 0 THEN
        -- Insert the record if it does not already exist
        INSERT INTO member_channel_access (channel_id, member_id, planet_id, user_id)
        VALUES (p_target_id, p_member_id, v_access_result.planet_id, v_access_result.user_id)
        ON CONFLICT (member_id, channel_id) DO NOTHING;
    ELSE
        -- Delete the record if it exists
        DELETE FROM member_channel_access
        WHERE member_id = p_member_id AND channel_id = p_target_id;
    END IF;

    RETURN v_access_result.value;
END;
$$;

alter function apply_member_access(bigint, bigint) owner to "valour-user";

