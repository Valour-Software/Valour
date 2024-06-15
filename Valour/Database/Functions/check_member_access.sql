create function check_member_access(p_member_id bigint, p_target_id bigint)
    returns TABLE(value integer, planet_id bigint, user_id bigint)
    language plpgsql
as
$$
DECLARE
BEGIN
    return QUERY SELECT * FROM check_member_permission(p_member_id, p_target_id, 1, null);
END;
$$;

alter function check_member_access(bigint, bigint) owner to "valour-user";

