-- =====================================================================
--  ÇAP / YDP Öğrenci Takip Otomasyonu - Veritabanı Şeması (PostgreSQL)
--  Selçuk Üniversitesi Teknoloji Fakültesi Bilgisayar Mühendisliği
--
--  Uygulama ilk açılışta bu dosyayı tek seferde çalıştırır (Infrastructure/Db.cs -> Migrate).
--  Dosyanın sonunda silme engeli ve değiştirilemez denetim kaydı tetikleyicileri bulunur.
-- =====================================================================

CREATE TABLE schema_version (
    version     INTEGER NOT NULL,
    applied_at  TIMESTAMP NOT NULL
);

-- Akademik dönemler (ör. 2025-2026 Güz)
CREATE TABLE terms (
    id              BIGSERIAL PRIMARY KEY,
    academic_year   VARCHAR(9)  NOT NULL,          -- 2025-2026
    season          VARCHAR(10) NOT NULL,          -- Güz | Bahar | Yaz
    sort_key        INTEGER     NOT NULL,          -- 20251, 20252, 20253
    is_current      SMALLINT    NOT NULL DEFAULT 0,
    is_active       SMALLINT    NOT NULL DEFAULT 1,
    created_at      TIMESTAMP   NOT NULL,
    UNIQUE (academic_year, season)
);

-- Öğrenci (ÇAP veya YDP). Öğrenci numarası benzersiz anahtardır (OBİS'e hazır).
CREATE TABLE students (
    id                  BIGSERIAL PRIMARY KEY,
    student_no          VARCHAR(20)  NOT NULL UNIQUE,
    full_name           VARCHAR(150) NOT NULL,
    email               VARCHAR(150) NOT NULL,
    program_type        VARCHAR(3)   NOT NULL CHECK (program_type IN ('CAP','YDP')),
    admission_term_id   INTEGER      NOT NULL REFERENCES terms(id),
    main_faculty        VARCHAR(150),
    main_department     VARCHAR(150),
    main_program_years  SMALLINT     NOT NULL DEFAULT 4,
    status              VARCHAR(20)  NOT NULL DEFAULT 'aktif',
    -- aktif | donduruldu | mezun | sertifika | kaydi_silindi | ayrildi
    data_source         VARCHAR(20)  NOT NULL DEFAULT 'manuel',   -- manuel | csv | obis
    external_ref        VARCHAR(100),
    created_by          INTEGER,
    created_at          TIMESTAMP    NOT NULL,
    updated_at          TIMESTAMP    NOT NULL
);

CREATE TABLE users (
    id                    BIGSERIAL PRIMARY KEY,
    username              VARCHAR(60)  NOT NULL UNIQUE,
    password_hash         VARCHAR(255) NOT NULL,
    role                  VARCHAR(20)  NOT NULL CHECK (role IN ('koordinator','ogrenci','bolum_baskani')),
    full_name             VARCHAR(150) NOT NULL,
    email                 VARCHAR(150),
    student_id            INTEGER REFERENCES students(id),
    is_active             SMALLINT NOT NULL DEFAULT 1,
    must_change_password  SMALLINT NOT NULL DEFAULT 1,
    failed_attempts       INTEGER  NOT NULL DEFAULT 0,
    locked_until          TIMESTAMP,
    last_login_at         TIMESTAMP,
    password_changed_at   TIMESTAMP,
    created_at            TIMESTAMP NOT NULL
);

-- Ana dal GANO kayıtları (yalnızca koordinatör girer, kaynağıyla)
CREATE TABLE gano_records (
    id             BIGSERIAL PRIMARY KEY,
    student_id     INTEGER NOT NULL REFERENCES students(id),
    term_id        INTEGER NOT NULL REFERENCES terms(id),
    gano           NUMERIC(4,2) NOT NULL,
    source         VARCHAR(100) NOT NULL,      -- transkript | öğrenci işleri | OBİS ...
    note           VARCHAR(500),
    state          VARCHAR(10) NOT NULL DEFAULT 'gecerli',   -- gecerli | iptal
    cancel_reason  VARCHAR(500),
    entered_by     INTEGER NOT NULL,
    created_at     TIMESTAMP NOT NULL
);

-- Bölüm ders kataloğu
CREATE TABLE courses (
    id                BIGSERIAL PRIMARY KEY,
    code              VARCHAR(20)  NOT NULL UNIQUE,
    name              VARCHAR(200) NOT NULL,
    credit            NUMERIC(4,1) NOT NULL DEFAULT 0,   -- ulusal kredi
    ects              NUMERIC(4,1) NOT NULL,             -- AKTS
    semester          SMALLINT,
    category          VARCHAR(20)  NOT NULL DEFAULT 'zorunlu', -- zorunlu | teknik_secmeli | staj | diger
    program_scope     VARCHAR(10)  NOT NULL DEFAULT 'HER_IKISI', -- CAP | YDP | HER_IKISI
    in_cap_protocol   SMALLINT     NOT NULL DEFAULT 0,  -- varsayılan ÇAP protokolünde mi
    is_active         SMALLINT     NOT NULL DEFAULT 1,
    description       VARCHAR(500),
    created_at        TIMESTAMP NOT NULL,
    updated_at        TIMESTAMP NOT NULL
);

-- Öğrencinin alacağı dersler listesi (protokol / ders planı)
CREATE TABLE student_courses (
    id               BIGSERIAL PRIMARY KEY,
    student_id       INTEGER NOT NULL REFERENCES students(id),
    course_id        INTEGER NOT NULL REFERENCES courses(id),
    state            VARCHAR(20) NOT NULL DEFAULT 'onay_bekliyor',
    -- onay_bekliyor | onaylandi | reddedildi | iptal | cikarildi
    added_reason     VARCHAR(500),
    reject_reason    VARCHAR(500),
    removed_reason   VARCHAR(500),
    is_legacy        SMALLINT NOT NULL DEFAULT 0,
    created_by       INTEGER NOT NULL,
    created_at       TIMESTAMP NOT NULL,
    decided_by       INTEGER,
    decided_at       TIMESTAMP,
    removed_by       INTEGER,
    removed_at       TIMESTAMP
);

-- Ders denemeleri: bir ders birden fazla kez alınabilir, her deneme ayrı satır
CREATE TABLE enrollments (
    id               BIGSERIAL PRIMARY KEY,
    student_id       INTEGER NOT NULL REFERENCES students(id),
    course_id        INTEGER NOT NULL REFERENCES courses(id),
    term_id          INTEGER NOT NULL REFERENCES terms(id),
    attempt_no       SMALLINT NOT NULL DEFAULT 1,
    status           VARCHAR(12) NOT NULL DEFAULT 'alindi',
    -- planlandi | alindi | gecti | kaldi | muaf | cikarildi
    letter_grade     VARCHAR(3),
    grade_point      NUMERIC(3,2),
    passed_term_id   INTEGER REFERENCES terms(id),
    approval_state   VARCHAR(15) NOT NULL DEFAULT 'onay_bekliyor',
    -- onay_bekliyor | onaylandi | reddedildi | iptal
    reject_reason    VARCHAR(500),
    cancel_reason    VARCHAR(500),
    note             VARCHAR(500),
    is_legacy        SMALLINT NOT NULL DEFAULT 0,
    correction_count SMALLINT NOT NULL DEFAULT 0,
    created_by       INTEGER NOT NULL,
    created_at       TIMESTAMP NOT NULL,
    approved_by      INTEGER,
    approved_at      TIMESTAMP,
    updated_at       TIMESTAMP NOT NULL
);

-- Belgeler (meta veri) - içerik ayrı tabloda
CREATE TABLE documents (
    id                BIGSERIAL PRIMARY KEY,
    student_id        INTEGER NOT NULL REFERENCES students(id),
    doc_type          VARCHAR(40)  NOT NULL,
    term_id           INTEGER REFERENCES terms(id),
    title             VARCHAR(200) NOT NULL,
    original_name     VARCHAR(255) NOT NULL,
    mime_type         VARCHAR(100) NOT NULL,
    size_bytes        INTEGER NOT NULL,
    sha256            VARCHAR(64) NOT NULL,
    state             VARCHAR(12) NOT NULL DEFAULT 'bekliyor', -- bekliyor | onaylandi | reddedildi | iptal
    reject_reason     VARCHAR(500),
    version_no        SMALLINT NOT NULL DEFAULT 1,
    supersedes_id     INTEGER REFERENCES documents(id),
    request_id        INTEGER,
    uploaded_by       INTEGER NOT NULL,
    uploaded_by_role  VARCHAR(20) NOT NULL,
    created_at        TIMESTAMP NOT NULL,
    decided_by        INTEGER,
    decided_at        TIMESTAMP
);

CREATE TABLE document_blobs (
    document_id  INTEGER PRIMARY KEY REFERENCES documents(id),
    content      BYTEA NOT NULL
);

-- Koordinatörün öğrenciden ek belge/bilgi isteği
CREATE TABLE document_requests (
    id                     BIGSERIAL PRIMARY KEY,
    student_id             INTEGER NOT NULL REFERENCES students(id),
    doc_type               VARCHAR(40) NOT NULL,
    description            VARCHAR(1000) NOT NULL,
    due_date               DATE,
    state                  VARCHAR(12) NOT NULL DEFAULT 'bekliyor', -- bekliyor | yuklendi | onaylandi | reddedildi | iptal
    fulfilled_document_id  INTEGER REFERENCES documents(id),
    response_note          VARCHAR(500),
    created_by             INTEGER NOT NULL,
    created_at             TIMESTAMP NOT NULL,
    updated_at             TIMESTAMP NOT NULL
);

-- Dönem sonu not girişleri (öğrenci girer, koordinatör onaylar)
CREATE TABLE grade_submissions (
    id                      BIGSERIAL PRIMARY KEY,
    enrollment_id           INTEGER NOT NULL REFERENCES enrollments(id),
    student_id              INTEGER NOT NULL REFERENCES students(id),
    term_id                 INTEGER NOT NULL REFERENCES terms(id),
    letter_grade            VARCHAR(3) NOT NULL,
    transcript_document_id  INTEGER NOT NULL REFERENCES documents(id),
    state                   VARCHAR(15) NOT NULL DEFAULT 'onay_bekliyor', -- onay_bekliyor | onaylandi | reddedildi | iptal
    reject_reason           VARCHAR(500),
    submitted_by            INTEGER NOT NULL,
    created_at              TIMESTAMP NOT NULL,
    decided_by              INTEGER,
    decided_at              TIMESTAMP
);

-- Katalogda olmayan ders için öğrenci talebi
CREATE TABLE course_requests (
    id                 BIGSERIAL PRIMARY KEY,
    student_id         INTEGER NOT NULL REFERENCES students(id),
    course_code        VARCHAR(20),
    course_name        VARCHAR(200) NOT NULL,
    explanation        VARCHAR(1000) NOT NULL,
    state              VARCHAR(12) NOT NULL DEFAULT 'bekliyor', -- bekliyor | eklendi | reddedildi
    response           VARCHAR(500),
    created_course_id  INTEGER REFERENCES courses(id),
    created_at         TIMESTAMP NOT NULL,
    decided_by         INTEGER,
    decided_at         TIMESTAMP
);

-- Sistem içi mesajlaşma (kalıcı, silinemez). Her öğrencinin tek bir yazışma dizisi vardır.
CREATE TABLE messages (
    id              BIGSERIAL PRIMARY KEY,
    student_id      INTEGER NOT NULL REFERENCES students(id),
    sender_id       INTEGER NOT NULL REFERENCES users(id),
    sender_role     VARCHAR(20) NOT NULL,
    body            TEXT NOT NULL,
    document_id     INTEGER REFERENCES documents(id),
    created_at      TIMESTAMP NOT NULL,
    read_at         TIMESTAMP
);

-- Uygulama içi bildirimler
CREATE TABLE notifications (
    id          BIGSERIAL PRIMARY KEY,
    user_id     INTEGER NOT NULL REFERENCES users(id),
    message     VARCHAR(500) NOT NULL,
    link        VARCHAR(255),
    created_at  TIMESTAMP NOT NULL,
    read_at     TIMESTAMP
);

-- Koordinatörün öğrenci kartına özel notu (öğrenci görmez)
CREATE TABLE coordinator_notes (
    id             BIGSERIAL PRIMARY KEY,
    student_id     INTEGER NOT NULL REFERENCES students(id),
    author_id      INTEGER NOT NULL REFERENCES users(id),
    body           TEXT NOT NULL,
    state          VARCHAR(10) NOT NULL DEFAULT 'aktif',  -- aktif | iptal
    created_at     TIMESTAMP NOT NULL
);

-- Yönerge kuralları (sürümlü). Eski sürümler korunur, yalnızca yenisi eklenir.
CREATE TABLE rule_sets (
    id            BIGSERIAL PRIMARY KEY,
    program_type  VARCHAR(3) NOT NULL CHECK (program_type IN ('CAP','YDP')),
    version_no    INTEGER NOT NULL,
    title         VARCHAR(200) NOT NULL,
    params        TEXT NOT NULL,          -- JSON
    valid_from    DATE NOT NULL,
    is_active     SMALLINT NOT NULL DEFAULT 0,
    note          VARCHAR(1000),
    created_by    INTEGER,
    created_at    TIMESTAMP NOT NULL,
    UNIQUE (program_type, version_no)
);

-- Yapılan kural kontrollerinin anlık görüntüsü (değiştirilemez)
CREATE TABLE rule_checks (
    id            BIGSERIAL PRIMARY KEY,
    student_id    INTEGER NOT NULL REFERENCES students(id),
    rule_set_id   INTEGER NOT NULL REFERENCES rule_sets(id),
    trigger_event VARCHAR(100) NOT NULL,
    summary_level VARCHAR(10) NOT NULL,   -- ok | bilgi | uyari | kritik
    results       TEXT NOT NULL,          -- JSON
    run_by        INTEGER,
    created_at    TIMESTAMP NOT NULL
);

-- Koordinatör kararları: mezuniyet, sertifika, kayıt silme, dondurma ...
CREATE TABLE decisions (
    id              BIGSERIAL PRIMARY KEY,
    student_id      INTEGER NOT NULL REFERENCES students(id),
    decision_type   VARCHAR(20) NOT NULL,
    term_id         INTEGER REFERENCES terms(id),
    decision_date   DATE NOT NULL,
    reference_no    VARCHAR(100),
    reason          VARCHAR(1000) NOT NULL,
    document_id     INTEGER REFERENCES documents(id),
    previous_status VARCHAR(20) NOT NULL,
    new_status      VARCHAR(20) NOT NULL,
    state           VARCHAR(10) NOT NULL DEFAULT 'gecerli',
    decided_by      INTEGER NOT NULL,
    created_at      TIMESTAMP NOT NULL
);

-- Basit anahtar/değer sistem ayarları
CREATE TABLE settings (
    setting_key    VARCHAR(60) PRIMARY KEY,
    setting_value  VARCHAR(1000) NOT NULL,
    description    VARCHAR(300),
    updated_by     INTEGER,
    updated_at     TIMESTAMP NOT NULL
);

-- İŞLEM KAYDI (denetim). Değiştirilemez ve silinemez (tetikleyici ile korunur).
CREATE TABLE audit_log (
    id            BIGSERIAL PRIMARY KEY,
    created_at    TIMESTAMP NOT NULL,
    user_id       INTEGER,
    username      VARCHAR(60),
    user_role     VARCHAR(20),
    action        VARCHAR(60) NOT NULL,
    entity_type   VARCHAR(40) NOT NULL,
    entity_id     INTEGER,
    student_id    INTEGER,
    old_values    TEXT,
    new_values    TEXT,
    reason        VARCHAR(1000),
    ip_address    VARCHAR(45),
    user_agent    VARCHAR(255)
);

-- Oturumlar (veritabanında tutulur; sunucu yeniden başlasa da korunur)
CREATE TABLE sessions (
    id             VARCHAR(128) PRIMARY KEY,
    data           TEXT NOT NULL,
    last_activity  INTEGER NOT NULL
);

-- Giriş denemesi sınırlaması (kaba kuvvet koruması)
CREATE TABLE login_attempts (
    id           BIGSERIAL PRIMARY KEY,
    ip_address   VARCHAR(45) NOT NULL,
    username     VARCHAR(60),
    success      SMALLINT NOT NULL,
    created_at   TIMESTAMP NOT NULL
);

CREATE INDEX ix_enr_student   ON enrollments(student_id);
CREATE INDEX ix_enr_course    ON enrollments(course_id);
CREATE INDEX ix_sc_student    ON student_courses(student_id);
CREATE INDEX ix_doc_student   ON documents(student_id);
CREATE INDEX ix_msg_student   ON messages(student_id);
CREATE INDEX ix_notif_user    ON notifications(user_id);
CREATE INDEX ix_audit_student ON audit_log(student_id);
CREATE INDEX ix_audit_created ON audit_log(created_at);
CREATE INDEX ix_gano_student  ON gano_records(student_id);
CREATE INDEX ix_login_ip      ON login_attempts(ip_address, created_at);

-- =====================================================================
--  "Hiçbir veri tamamen silinmemeli": iş tablolarında DELETE/TRUNCATE yasak.
--  İşlem kaydı (audit_log), kural kontrolleri ve belge içerikleri değiştirilemez.
-- =====================================================================
CREATE OR REPLACE FUNCTION cap_no_delete() RETURNS trigger AS $$
BEGIN RAISE EXCEPTION 'Bu tablodan kayit silinemez: %', TG_TABLE_NAME; END; $$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION cap_no_update() RETURNS trigger AS $$
BEGIN RAISE EXCEPTION 'Bu tablo degistirilemez: %', TG_TABLE_NAME; END; $$ LANGUAGE plpgsql;

CREATE TRIGGER trg_terms_nodel BEFORE DELETE ON terms FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_terms_notrunc BEFORE TRUNCATE ON terms FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_students_nodel BEFORE DELETE ON students FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_students_notrunc BEFORE TRUNCATE ON students FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_users_nodel BEFORE DELETE ON users FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_users_notrunc BEFORE TRUNCATE ON users FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_gano_records_nodel BEFORE DELETE ON gano_records FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_gano_records_notrunc BEFORE TRUNCATE ON gano_records FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_courses_nodel BEFORE DELETE ON courses FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_courses_notrunc BEFORE TRUNCATE ON courses FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_student_courses_nodel BEFORE DELETE ON student_courses FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_student_courses_notrunc BEFORE TRUNCATE ON student_courses FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_enrollments_nodel BEFORE DELETE ON enrollments FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_enrollments_notrunc BEFORE TRUNCATE ON enrollments FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_documents_nodel BEFORE DELETE ON documents FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_documents_notrunc BEFORE TRUNCATE ON documents FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_document_blobs_nodel BEFORE DELETE ON document_blobs FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_document_blobs_notrunc BEFORE TRUNCATE ON document_blobs FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_document_requests_nodel BEFORE DELETE ON document_requests FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_document_requests_notrunc BEFORE TRUNCATE ON document_requests FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_grade_submissions_nodel BEFORE DELETE ON grade_submissions FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_grade_submissions_notrunc BEFORE TRUNCATE ON grade_submissions FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_course_requests_nodel BEFORE DELETE ON course_requests FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_course_requests_notrunc BEFORE TRUNCATE ON course_requests FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_messages_nodel BEFORE DELETE ON messages FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_messages_notrunc BEFORE TRUNCATE ON messages FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_coordinator_notes_nodel BEFORE DELETE ON coordinator_notes FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_coordinator_notes_notrunc BEFORE TRUNCATE ON coordinator_notes FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_rule_sets_nodel BEFORE DELETE ON rule_sets FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_rule_sets_notrunc BEFORE TRUNCATE ON rule_sets FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_rule_checks_nodel BEFORE DELETE ON rule_checks FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_rule_checks_notrunc BEFORE TRUNCATE ON rule_checks FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_decisions_nodel BEFORE DELETE ON decisions FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_decisions_notrunc BEFORE TRUNCATE ON decisions FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_settings_nodel BEFORE DELETE ON settings FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_settings_notrunc BEFORE TRUNCATE ON settings FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_audit_log_nodel BEFORE DELETE ON audit_log FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_audit_log_notrunc BEFORE TRUNCATE ON audit_log FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_notifications_nodel BEFORE DELETE ON notifications FOR EACH ROW EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_notifications_notrunc BEFORE TRUNCATE ON notifications FOR EACH STATEMENT EXECUTE FUNCTION cap_no_delete();
CREATE TRIGGER trg_audit_log_noupd BEFORE UPDATE ON audit_log FOR EACH ROW EXECUTE FUNCTION cap_no_update();
CREATE TRIGGER trg_rule_checks_noupd BEFORE UPDATE ON rule_checks FOR EACH ROW EXECUTE FUNCTION cap_no_update();
CREATE TRIGGER trg_document_blobs_noupd BEFORE UPDATE ON document_blobs FOR EACH ROW EXECUTE FUNCTION cap_no_update();

INSERT INTO schema_version (version, applied_at) VALUES (1, NOW());
