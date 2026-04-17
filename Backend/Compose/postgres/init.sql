CREATE DATABASE aidb;
CREATE DATABASE userdb;
CREATE DATABASE notificationdb;
CREATE DATABASE feeddb;

-- Ensure the default postgres user has access
GRANT ALL PRIVILEGES ON DATABASE aidb TO postgres;
GRANT ALL PRIVILEGES ON DATABASE userdb TO postgres;
GRANT ALL PRIVILEGES ON DATABASE notificationdb TO postgres;
GRANT ALL PRIVILEGES ON DATABASE feeddb TO postgres;
