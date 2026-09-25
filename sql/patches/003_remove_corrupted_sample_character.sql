-- The dump ships one sample character ("Aaldyienie", account_id 1). Its profile blob went through the
-- same latin1 -> UTF-8 corruption as items.raw_data (name prefixed with U+FFFD bytes, invalid race/class),
-- and account_id 1 is also the first account a fresh install creates, so that player sees a character
-- that cannot enter the world. Remove it.
DELETE FROM character_ WHERE name = 'Aaldyienie' AND account_id = 1;
