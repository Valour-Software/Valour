create function apply_member_access_planet(p_member_id bigint) returns integer
    language plpgsql
as
$$
DECLARE
    v_current_channel_id BIGINT;
    v_changed_count INTEGER;
    v_planet_id BIGINT;
BEGIN
    -- First get planet id from member
    SELECT planet_id INTO v_planet_id
    FROM planet_members WHERE id = p_member_id;

    v_changed_count := 0;

    FOR v_current_channel_id IN
        SELECT id FROM channels WHERE planet_id = v_planet_id
        LOOP
            PERFORM apply_member_access(p_member_id, v_current_channel_id);
            v_changed_count := v_changed_count + 1;
        END LOOP;

    RETURN v_changed_count;
END;
$$;

alter function apply_member_access_planet(bigint) owner to "valour-user";

