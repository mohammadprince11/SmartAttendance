/* =====================================================================
   تشخيص انتماء الموظفين للشركات — قراءة فقط (SELECT حصراً)

   الغرض: قبل إضافة عمود CompanyId لجدول Employees وترحيل البيانات، يجب
   معرفة الحالات الشاذّة. **لا يعدّل هذا السكربت أي صفّ ولا مخطط.**

   شغّله على نسخة الإنتاج (أو نسخة حديثة منها) وأرسل نتائج الاستعلامات
   السبعة. القرار الهندسي يتغيّر بحسبها:
   - إن كان (2) و(3) و(4) أصفاراً ⟹ الترحيل مباشر من الفرع بلا استثناءات.
   - إن ظهرت صفوف ⟹ نحتاج قاعدة صريحة لكل حالة قبل كتابة الهجرة.
   ===================================================================== */

-- (1) كم شركة فعلاً؟ وكم موظفاً بكل شركة عبر الفرع؟
SELECT
    c.Id            AS CompanyId,
    c.Name          AS CompanyName,
    COUNT(e.Id)     AS EmployeeCount
FROM Companies c
LEFT JOIN Branches  b ON b.CompanyId = c.Id
LEFT JOIN Employees e ON e.BranchId  = b.Id AND e.IsDeleted = 0
GROUP BY c.Id, c.Name
ORDER BY EmployeeCount DESC;

-- (2) 🔴 موظفون بلا فرع صالح ⟹ لا يمكن اشتقاق شركتهم إطلاقاً
SELECT COUNT(*) AS EmployeesWithoutValidBranch
FROM Employees e
WHERE e.IsDeleted = 0
  AND (e.BranchId IS NULL
       OR e.BranchId = 0
       OR NOT EXISTS (SELECT 1 FROM Branches b WHERE b.Id = e.BranchId));

-- (2ب) عيّنة منهم (10 صفوف) للفحص اليدوي — بلا أسماء كاملة تفادياً لتسريب بيانات
SELECT TOP 10
    e.Id,
    e.EmployeeNo,
    e.BranchId,
    e.DepartmentId
FROM Employees e
WHERE e.IsDeleted = 0
  AND (e.BranchId IS NULL
       OR e.BranchId = 0
       OR NOT EXISTS (SELECT 1 FROM Branches b WHERE b.Id = e.BranchId))
ORDER BY e.Id;

-- (3) 🔴 فروع بلا شركة صالحة ⟹ موظفوها بلا انتماء
SELECT COUNT(*) AS BranchesWithoutValidCompany
FROM Branches b
WHERE b.CompanyId IS NULL
   OR b.CompanyId = 0
   OR NOT EXISTS (SELECT 1 FROM Companies c WHERE c.Id = b.CompanyId);

-- (4) 🟠 تعارض: شركة القسم ≠ شركة الفرع لنفس الموظف
--     (يحدّد أيّ المصدرين نعتمده قاعدةً عند الترحيل)
SELECT COUNT(*) AS EmployeesWithCompanyConflict
FROM Employees e
JOIN Branches    b ON b.Id = e.BranchId
JOIN Departments d ON d.Id = e.DepartmentId
WHERE e.IsDeleted = 0
  AND d.CompanyId <> b.CompanyId;

-- (4ب) عيّنة التعارض
SELECT TOP 10
    e.Id,
    e.EmployeeNo,
    b.CompanyId AS BranchCompanyId,
    d.CompanyId AS DepartmentCompanyId
FROM Employees e
JOIN Branches    b ON b.Id = e.BranchId
JOIN Departments d ON d.Id = e.DepartmentId
WHERE e.IsDeleted = 0
  AND d.CompanyId <> b.CompanyId
ORDER BY e.Id;

-- (5) 🟠 أقسام مرتبطة بفرع من شركة أخرى (خلل مرجعي بالهيكل نفسه)
SELECT COUNT(*) AS DepartmentsCrossingCompanies
FROM Departments d
JOIN Branches b ON b.Id = d.BranchId
WHERE d.BranchId IS NOT NULL
  AND d.CompanyId <> b.CompanyId;

-- (6) هل يعمل مستخدمو الباك أوفيس عبر أكثر من شركة؟
--     (يحدّد هل نحتاج «شركة نشطة» بالجلسة أم شركة واحدة لكل مستخدم)
SELECT
    su.Id                       AS SystemUserId,
    su.UserName,
    COUNT(DISTINCT b.CompanyId) AS DistinctCompanies
FROM SystemUsers su
LEFT JOIN Employees e ON e.Id = su.EmployeeId
LEFT JOIN Branches  b ON b.Id = e.BranchId
GROUP BY su.Id, su.UserName
HAVING COUNT(DISTINCT b.CompanyId) > 1;

-- (7) حجم الأثر: كم صفّاً بالجداول الحسّاسة مرتبط بموظفين؟
--     (يقدّر كلفة فرض CompanyId لاحقاً بكل استعلام)
SELECT 'Employees' AS TableName, COUNT(*) AS Rows FROM Employees WHERE IsDeleted = 0
UNION ALL SELECT 'EmployeeDocuments', COUNT(*) FROM EmployeeDocuments
UNION ALL SELECT 'DayAttendance',     COUNT(*) FROM DayAttendance
UNION ALL SELECT 'SelfServiceRequests', COUNT(*) FROM SelfServiceRequests;
