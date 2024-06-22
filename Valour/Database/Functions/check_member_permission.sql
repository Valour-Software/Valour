create function check_member_permission(p_member_id bigint, p_target_id bigint, p_permission_value bigint, p_permission_type integer)
    returns TABLE(value integer, planet_id bigint, user_id bigint)
    language plpgsql
as
$$
DECLARE
    v_role_ids BIGINT[];
    v_yes_position INT;
    v_no_position INT;
    v_top_role planet_roles%ROWTYPE;
    v_top_role_perms BIGINT;
    v_planet_id BIGINT;
    v_is_admin BOOLEAN;
    v_channel RECORD;
    v_target_id BIGINT := p_target_id;
    v_final_perm_type INTEGER;
    v_member RECORD;
    v_user_id BIGINT;
BEGIN

    -- Pull down the single record for the member
    SELECT * INTO v_member
    FROM planet_members
    WHERE id = p_member_id;

    -- If the member record is null, return 15
    IF NOT FOUND THEN
        RETURN QUERY SELECT 15 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
        RETURN;
    END IF;

    -- Assign user_id from the member record
    v_user_id := v_member.user_id;

    -- Traverse up the parent chain until we find a channel that does not inherit permissions or has no parent
    LOOP
        SELECT * INTO v_channel
        FROM channels
        WHERE id = v_target_id;

        EXIT WHEN NOT FOUND OR v_channel.inherits_perms IS NULL OR v_channel.inherits_perms = FALSE OR v_channel.parent_id IS NULL;

        v_target_id := v_channel.parent_id;
        v_planet_id := v_channel.planet_id;
    END LOOP;

    -- Get the planet ID from the final channel
    v_planet_id := v_channel.planet_id;

    -- Determine the final target type
    -- This sets to the default if the declared type is null
    -- For example, a category will default to category permissions
    -- RAISE NOTICE 'Value: %', v_channel.channel_type;
    -- RAISE NOTICE 'Value: %', v_channel.id;
    v_final_perm_type := COALESCE(p_permission_type, v_channel.channel_type);

    -- Check if the member is the owner of the planet
    IF EXISTS (SELECT 1
               FROM planets p
                        JOIN planet_members pm ON p.owner_id = pm.user_id
               WHERE p.id = v_planet_id AND pm.id = p_member_id) THEN
        RETURN QUERY SELECT 6 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
        RETURN;
    END IF;

    -- Get role ids for member and check if the member is an admin in any role
    SELECT array_agg(role_id), bool_or(r.is_admin) INTO v_role_ids, v_is_admin
    FROM planet_role_members prm
             JOIN planet_roles r ON prm.role_id = r.id
    WHERE prm.member_id = p_member_id
    GROUP BY prm.member_id;

    -- Admins always have permission
    IF v_is_admin THEN
        RETURN QUERY SELECT 4 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
        RETURN;
    END IF;

    -- Get the highest authority (lowest position) role the member has which says they have permission
    SELECT MIN(r.position) INTO v_yes_position
    FROM permissions_nodes pn
             JOIN planet_roles r ON pn.role_id = r.id
    WHERE pn.target_id = p_target_id
      AND pn.target_type = v_final_perm_type
      AND pn.role_id = ANY(v_role_ids)
      AND (pn.mask & p_permission_value) = p_permission_value
      AND (pn.code & p_permission_value) = p_permission_value;

    -- Get the highest authority (lowest position) role the member has which says they do NOT have permission
    SELECT MIN(r.position) INTO v_no_position
    FROM permissions_nodes pn
             JOIN planet_roles r ON pn.role_id = r.id
    WHERE pn.target_id = p_target_id
      AND pn.target_type = v_final_perm_type
      AND pn.role_id = ANY(v_role_ids)
      AND (pn.mask & p_permission_value) = p_permission_value
      AND (pn.code & p_permission_value) = 0;

    -- If the NO permission exists
    IF v_no_position IS NOT NULL THEN
        -- If the YES permission exists, compare the two
        IF v_yes_position IS NOT NULL THEN
            IF v_yes_position < v_no_position THEN
                RETURN QUERY SELECT 8 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
                RETURN;
            ELSE
                RETURN QUERY SELECT 7 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
                RETURN;
            END IF;
        ELSE
            RETURN QUERY SELECT 9 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
            RETURN;
        END IF;
    ELSE
        -- If the NO permission does not exist, but the YES does, return true
        IF v_yes_position IS NOT NULL THEN
            RETURN QUERY SELECT 10 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
            RETURN;
        ELSE
            -- Get the top role the member has
            SELECT r.*
            INTO v_top_role
            FROM planet_role_members prm
                     JOIN planet_roles r ON prm.role_id = r.id
            WHERE prm.member_id = p_member_id
            ORDER BY r.position
            LIMIT 1;

            -- Check if the top role exists, otherwise return false
            IF FOUND THEN
                -- Get the appropriate permission field based on p_permission_type
                CASE v_final_perm_type
                    WHEN 0 THEN v_top_role_perms := v_top_role.chat_perms;
                    WHEN 1 THEN v_top_role_perms := v_top_role.cat_perms;
                    WHEN 2 THEN v_top_role_perms := v_top_role.voice_perms;
                                RETURN QUERY SELECT 11 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id; -- Invalid permission type
                                RETURN;
                    END CASE;

                -- Check if the top role has the desired permission
                IF (v_top_role_perms & p_permission_value) = p_permission_value THEN
                    RETURN QUERY SELECT 12 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
                    RETURN;
                ELSE
                    RETURN QUERY SELECT 13 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
                    RETURN;
                END IF;
            ELSE
                RETURN QUERY SELECT -1 AS VALUE, v_planet_id AS planet_id, v_user_id AS user_id;
                RETURN;
            END IF;
        END IF;
    END IF;
END;
$$;

alter function check_member_permission(bigint, bigint, bigint, integer) owner to "valour-user";

