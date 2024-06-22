create function apply_member_access_for_all_in_role(p_role_id bigint) returns integer
    language plpgsql
as
$$
DECLARE
    v_current_member_id BIGINT;
    v_changed_count INTEGER;
BEGIN

    v_changed_count := 0;

    -- Go over all members who have the role
    FOR v_current_member_id IN
        SELECT member_id FROM planet_role_members WHERE role_id = p_role_id
        LOOP
            -- Update all channels in planet for member
            PERFORM apply_member_access_planet(v_current_member_id);
            v_changed_count := v_changed_count + 1;
        END LOOP;

    RETURN v_changed_count;
END;
$$;

alter function apply_member_access_for_all_in_role(bigint) owner to "valour-user";

