create function apply_member_access_channel_all(p_channel_id bigint) returns integer
    language plpgsql
as
$$
DECLARE
    v_current_member_id BIGINT;
    v_changed_count INTEGER;
    v_planet_id BIGINT;
BEGIN

    -- First get planet id from channel
    SELECT planet_id INTO v_planet_id
    FROM channels WHERE id = p_channel_id;

    v_changed_count := 0;

    -- Go over all members in planet
    FOR v_current_member_id IN
        SELECT id FROM planet_members WHERE planet_id = v_planet_id
        LOOP
            -- Update all channels in planet for member
            PERFORM apply_member_access(v_current_member_id, p_channel_id);
            v_changed_count := v_changed_count + 1;
        END LOOP;

    RETURN v_changed_count;
END;
$$;

alter function apply_member_access_channel_all(bigint) owner to "valour-user";

