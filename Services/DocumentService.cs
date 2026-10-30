namespace CapYdp.Services;

/// <summary>
/// Belge arşivi. Belgeler hiçbir zaman silinmez; yenilenen belgenin eski sürümü arşivde kalır.
/// Dosya içerikleri veritabanında saklanır (bulut sunucularında kalıcı disk gerekmez).
/// </summary>
public static class DocumentService
{
    public static long Store(IAppCtx c, long studentId, string docType, long? termId, string title, CheckedFile file, long? requestId = null, long? supersedesId = null)
    {
        var u = c.User!;
        if (!L.DocTypes.ContainsKey(docType))
        {
            throw new UserError("Belge türü geçersiz.");
        }
        if (docType == "cap_ydp_transkript" && termId == null)
        {
            throw new UserError("ÇAP/YDP not belgesi için ilgili dönem seçilmelidir.");
        }
        if (termId != null && Terms.Find(c, termId.Value) == null)
        {
            throw new UserError("Dönem geçersiz.");
        }
        if (requestId != null && c.Db.One("SELECT id FROM document_requests WHERE id = @id AND student_id = @sid AND state IN ('bekliyor','reddedildi')", new { id = requestId, sid = studentId }) == null)
        {
            throw new UserError("Yanıtlanacak belge isteği bulunamadı.");
        }
        var version = 1;
        if (supersedesId != null)
        {
            var prev = c.Db.One("SELECT * FROM documents WHERE id = @id AND student_id = @sid", new { id = supersedesId, sid = studentId }) ?? throw new UserError("Yenilenecek belge bulunamadı.");
            version = prev.I("version_no") + 1;
        }
        var isCoord = u.Role == "koordinator";
        title = (title ?? "").Trim();
        title = title != "" ? (title.Length > 200 ? title[..200] : title) : L.DocTypes[docType];

        return c.Db.Tx(() =>
        {
            var id = c.Db.Insert("documents", new Data
            {
                ["student_id"] = studentId,
                ["doc_type"] = docType,
                ["term_id"] = termId,
                ["title"] = title,
                ["original_name"] = file.Name,
                ["mime_type"] = file.Mime,
                ["size_bytes"] = (int)file.Size,
                ["sha256"] = file.Sha256,
                ["state"] = isCoord ? "onaylandi" : "bekliyor",
                ["version_no"] = version,
                ["supersedes_id"] = supersedesId,
                ["request_id"] = requestId,
                ["uploaded_by"] = u.Id,
                ["uploaded_by_role"] = u.Role,
                ["created_at"] = Clock.Now(),
                ["decided_by"] = isCoord ? u.Id : null,
                ["decided_at"] = isCoord ? Clock.Now() : null,
            });
            c.Db.InsertNoId("document_blobs", new Data { ["document_id"] = id, ["content"] = file.Data });
            if (requestId != null)
            {
                c.Db.Update("document_requests", new Data { ["state"] = "yuklendi", ["fulfilled_document_id"] = id, ["updated_at"] = Clock.Now() }, "id = @id", new { id = requestId });
            }
            Audit.Log(c, "document_upload", "document", id, studentId, null, new Data
            {
                ["tur"] = L.DocTypes[docType], ["baslik"] = title, ["dosya"] = file.Name, ["boyut"] = file.Size,
                ["sha256"] = file.Sha256, ["surum"] = version, ["onceki"] = supersedesId,
            });
            if (!isCoord && docType != "mesaj_eki")
            {
                var st = c.Db.One("SELECT full_name FROM students WHERE id = @id", new { id = studentId })!;
                Notify.Coordinators(c, st.S("full_name") + " yeni belge yükledi: " + title, "/koordinator/ogrenci/" + studentId + "?tab=belgeler");
            }
            if (isCoord)
            {
                Notify.Student(c, studentId, "Koordinatör kartınıza belge ekledi: " + title, "/ogrenci/belgeler");
            }
            return id;
        });
    }

    public static long Decide(IAppCtx c, long id, bool approve, string reason = "")
    {
        var d = c.Db.One("SELECT * FROM documents WHERE id = @id AND state = 'bekliyor'", new { id }) ?? throw new UserError("Bekleyen belge bulunamadı.");
        if (!approve)
        {
            StudentService.RequireReason(reason);
        }
        var state = approve ? "onaylandi" : "reddedildi";
        c.Db.Update("documents", new Data { ["state"] = state, ["reject_reason"] = approve ? null : reason, ["decided_by"] = c.User!.Id, ["decided_at"] = Clock.Now() }, "id = @id", new { id });
        if (!d.IsNull("request_id"))
        {
            c.Db.Update("document_requests", new Data { ["state"] = state, ["response_note"] = approve ? null : reason, ["updated_at"] = Clock.Now() }, "id = @id", new { id = d.L("request_id") });
        }
        Audit.Log(c, approve ? "document_approve" : "document_reject", "document", id, d.L("student_id"), new Data { ["state"] = "bekliyor" },
            new Data { ["state"] = state, ["belge"] = d.S("title") }, reason == "" ? null : reason);
        Notify.Student(c, d.L("student_id"), "Belgeniz \"" + d.S("title") + "\" " + (approve ? "onaylandı." : "reddedildi. Gerekçe: " + reason + " — Lütfen yeniden yükleyin."), "/ogrenci/belgeler");
        return d.L("student_id");
    }

    public static long Cancel(IAppCtx c, long id, string reason)
    {
        var d = c.Db.One("SELECT * FROM documents WHERE id = @id AND state <> 'iptal'", new { id }) ?? throw new UserError("Belge bulunamadı.");
        StudentService.RequireReason(reason);
        c.Db.Update("documents", new Data { ["state"] = "iptal", ["reject_reason"] = reason, ["decided_by"] = c.User!.Id, ["decided_at"] = Clock.Now() }, "id = @id", new { id });
        Audit.Log(c, "document_cancel", "document", id, d.L("student_id"), new Data { ["state"] = d.S("state") }, new Data { ["state"] = "iptal" }, reason);
        return d.L("student_id");
    }

    /// <summary>Yetki: öğrenci yalnızca kendi kartındaki belgeyi görebilir</summary>
    public static bool CanView(IAppCtx c, Row doc)
    {
        var u = c.User;
        if (u == null)
        {
            return false;
        }
        if (u.Role is "koordinator" or "bolum_baskani")
        {
            return true;
        }
        return u.Role == "ogrenci" && u.StudentId == doc.L("student_id");
    }

    public static byte[] Content(IAppCtx c, long id)
    {
        return (byte[]?)c.Db.Scalar("SELECT content FROM document_blobs WHERE document_id = @id", new { id }) ?? Array.Empty<byte>();
    }

    // ============ Ek belge istekleri ============

    public static void CreateRequest(IAppCtx c, long studentId, string docType, string description, string? due)
    {
        if (!L.DocTypes.ContainsKey(docType))
        {
            throw new UserError("Belge türü geçersiz.");
        }
        description = (description ?? "").Trim();
        if (description.Length < 5)
        {
            throw new UserError("İstek açıklaması en az 5 karakter olmalıdır.");
        }
        DateTime? dueDate = null;
        if (!string.IsNullOrWhiteSpace(due))
        {
            if (!DateTime.TryParseExact(due, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var dd))
            {
                throw new UserError("Son tarih geçersiz.");
            }
            dueDate = dd;
        }
        var id = c.Db.Insert("document_requests", new Data
        {
            ["student_id"] = studentId, ["doc_type"] = docType, ["description"] = description.Length > 1000 ? description[..1000] : description,
            ["due_date"] = dueDate, ["state"] = "bekliyor", ["created_by"] = c.User!.Id, ["created_at"] = Clock.Now(), ["updated_at"] = Clock.Now(),
        });
        Audit.Log(c, "request_create", "document_request", id, studentId, null, new Data { ["tur"] = L.DocTypes[docType], ["aciklama"] = description, ["son_tarih"] = due });
        Notify.Student(c, studentId, "Koordinatör sizden belge/bilgi istedi: " + L.DocTypes[docType], "/ogrenci/belgeler");
    }

    public static long CancelRequest(IAppCtx c, long id, string reason)
    {
        var r = c.Db.One("SELECT * FROM document_requests WHERE id = @id AND state IN ('bekliyor','yuklendi','reddedildi')", new { id }) ?? throw new UserError("İstek bulunamadı.");
        StudentService.RequireReason(reason);
        c.Db.Update("document_requests", new Data { ["state"] = "iptal", ["response_note"] = reason, ["updated_at"] = Clock.Now() }, "id = @id", new { id });
        Audit.Log(c, "request_update", "document_request", id, r.L("student_id"), new Data { ["state"] = r.S("state") }, new Data { ["state"] = "iptal" }, reason);
        return r.L("student_id");
    }
}

/// <summary>Koordinatör–öğrenci mesajlaşması. Mesajlar kalıcıdır, düzenlenemez ve silinemez.</summary>
public static class MessageService
{
    public static long Send(IAppCtx c, long studentId, string body, CheckedFile? file = null)
    {
        var u = c.User!;
        body = (body ?? "").Trim();
        if (body.Length < 1 && file == null)
        {
            throw new UserError("Mesaj boş olamaz.");
        }
        if (body.Length > 5000)
        {
            throw new UserError("Mesaj en fazla 5000 karakter olabilir.");
        }
        return c.Db.Tx(() =>
        {
            long? docId = null;
            if (file != null)
            {
                docId = DocumentService.Store(c, studentId, "mesaj_eki", null, "Mesaj eki: " + file.Name, file);
            }
            var id = c.Db.Insert("messages", new Data
            {
                ["student_id"] = studentId,
                ["sender_id"] = u.Id,
                ["sender_role"] = u.Role,
                ["body"] = body != "" ? body : "(ek dosya)",
                ["document_id"] = docId,
                ["created_at"] = Clock.Now(),
            });
            Audit.Log(c, "message_send", "message", id, studentId, null, new Data { ["uzunluk"] = body.Length, ["ek"] = docId });
            if (u.Role == "ogrenci")
            {
                Notify.Coordinators(c, "Yeni mesaj: " + u.FullName, "/koordinator/ogrenci/" + studentId + "?tab=mesajlar");
            }
            else
            {
                Notify.Student(c, studentId, "Koordinatörden yeni mesajınız var.", "/ogrenci/mesajlar");
            }
            return id;
        });
    }

    /// <summary>Karşı tarafın mesajlarını okundu olarak işaretler</summary>
    public static void MarkRead(IAppCtx c, long studentId, string viewerRole)
    {
        if (viewerRole == "ogrenci")
        {
            c.Db.Exec("UPDATE messages SET read_at = @t WHERE student_id = @sid AND sender_role <> 'ogrenci' AND read_at IS NULL", new { t = Clock.Now(), sid = studentId });
        }
        else if (viewerRole == "koordinator")
        {
            c.Db.Exec("UPDATE messages SET read_at = @t WHERE student_id = @sid AND sender_role = 'ogrenci' AND read_at IS NULL", new { t = Clock.Now(), sid = studentId });
        }
    }

    public static List<Row> Thread(IAppCtx c, long studentId) =>
        c.Db.All(@"SELECT m.*, u.full_name AS sender_name, d.original_name AS doc_name, d.size_bytes AS doc_size
                   FROM messages m JOIN users u ON u.id = m.sender_id LEFT JOIN documents d ON d.id = m.document_id
                   WHERE m.student_id = @sid ORDER BY m.id", new { sid = studentId });
}
