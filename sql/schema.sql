-- Esquema do banco do servidor WYD. É aplicado automaticamente quando o servidor inicia,
-- mas também pode ser executado à mão: mysql -u root -p wyd < sql/schema.sql

CREATE TABLE IF NOT EXISTS accounts (
    id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
    username      VARCHAR(16)  NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    access_level  TINYINT      NOT NULL DEFAULT 0,
    banned        TINYINT(1)   NOT NULL DEFAULT 0,
    cargo_coin    INT          NOT NULL DEFAULT 0,
    cargo         BLOB         NULL,          -- STRUCT_ITEM[128], 8 bytes cada
    created_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_login_at DATETIME     NULL,
    last_login_ip VARCHAR(45)  NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_accounts_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS characters (
    id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id  INT UNSIGNED NOT NULL,
    slot        TINYINT      NOT NULL,        -- 0..3
    name        VARCHAR(16)  NOT NULL,
    class       TINYINT      NOT NULL,        -- 0 TK, 1 FM, 2 BM, 3 HT
    level       INT          NOT NULL DEFAULT 0,
    exp         BIGINT       NOT NULL DEFAULT 0,
    coin        INT          NOT NULL DEFAULT 0,
    guild       SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    pos_x       SMALLINT     NOT NULL,
    pos_y       SMALLINT     NOT NULL,
    ac          INT          NOT NULL DEFAULT 0,
    damage      INT          NOT NULL DEFAULT 0,
    max_hp      INT          NOT NULL,
    max_mp      INT          NOT NULL,
    hp          INT          NOT NULL,
    mp          INT          NOT NULL,
    str         SMALLINT     NOT NULL,
    `int`       SMALLINT     NOT NULL,
    dex         SMALLINT     NOT NULL,
    con         SMALLINT     NOT NULL,
    equip       BLOB         NULL,            -- STRUCT_ITEM[16]
    inventory   BLOB         NULL,            -- STRUCT_ITEM[64]
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_characters_name (name),
    UNIQUE KEY uq_characters_slot (account_id, slot),
    CONSTRAINT fk_characters_account FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
