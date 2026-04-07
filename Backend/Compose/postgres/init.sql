CREATE DATABASE aidb;
CREATE DATABASE userdb;
CREATE DATABASE notificationdb;

-- Ensure the default postgres user has access
GRANT ALL PRIVILEGES ON DATABASE aidb TO postgres;
GRANT ALL PRIVILEGES ON DATABASE userdb TO postgres;
GRANT ALL PRIVILEGES ON DATABASE notificationdb TO postgres;
