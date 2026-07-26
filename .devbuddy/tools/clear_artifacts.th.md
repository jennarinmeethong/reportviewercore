# คู่มือ `clear_artifacts.py`

คู่มือภาษาไทยสำหรับไฟล์ `.devbuddy/tools/clear_artifacts.py` ซึ่งใช้ตรวจสอบและลบไฟล์ผลลัพธ์ที่สร้างขึ้นจากการทดสอบ ตัวอย่าง และการแพ็กเกจของโครงการ

## ใช้ทำอะไร

เครื่องมือนี้ลบรายการภายในโฟลเดอร์ `artifacts/` เช่น ไฟล์ XLSX, PDF, PNG, DOCX, HTML, ผลลัพธ์การทดสอบ และแพ็กเกจ NuGet เพื่อให้เริ่มรอบทดสอบใหม่จากผลลัพธ์ที่สะอาดได้

เครื่องมือนี้ไม่ลบ source code, โฟลเดอร์ `bin/obj` หรือโฟลเดอร์ `artifacts` เอง

## ข้อกำหนดด้านความปลอดภัย

- ค่าเริ่มต้นเป็นโหมดทดลอง (`dry-run`) จึงไม่ลบไฟล์
- ต้องระบุ `--apply` อย่างชัดเจนจึงจะลบจริง
- อนุญาตเฉพาะโฟลเดอร์ชื่อ `artifacts` ที่เป็นโฟลเดอร์ลูกโดยตรงของ repository root
- ลบเฉพาะรายการลูกโดยตรงของ `artifacts/` และลบไดเรกทอรีลูกแบบทั้งไดเรกทอรี
- ข้าม symbolic link และ junction เพื่อไม่ติดตามไปลบข้อมูลนอกขอบเขต
- สามารถเก็บรายการที่ต้องการไว้ด้วย `--keep NAME`
- หากระบุชื่อใน `--keep` ที่ไม่มีอยู่จริง โปรแกรมจะแจ้งข้อผิดพลาดแทนการทำงานต่อ

> คำเตือน: `--apply` เป็นการลบจริง ควรอ่านผลจาก dry-run ก่อนทุกครั้ง และปิดไฟล์ผลลัพธ์ที่เปิดอยู่ใน Excel/Word ก่อนลบ

## เริ่มต้นอย่างรวดเร็ว

รันจาก repository root (`C:\Codes\reportviewercore`):

```powershell
python .devbuddy\tools\clear_artifacts.py --summary
```

คำสั่งนี้แสดงจำนวนรายการที่จะลบ โดยไม่ลบอะไร เช่น:

```text
would remove 84 artifact entries; kept 0; skipped links 0
dry run only; pass --apply to delete generated entries
```

เมื่อตรวจสอบแล้วและต้องการลบจริง:

```powershell
python .devbuddy\tools\clear_artifacts.py --apply
```

## เก็บผลลัพธ์บางรายการไว้

ใช้ `--keep NAME` ซ้ำได้ โดย `NAME` ต้องเป็นชื่อลูกโดยตรงของ `artifacts/` เท่านั้น:

```powershell
python .devbuddy\tools\clear_artifacts.py --summary --keep test-results
python .devbuddy\tools\clear_artifacts.py --apply --keep test-results --keep feature-showcase
```

ตัวอย่างข้างต้นจะเก็บ `artifacts/test-results/` และ `artifacts/feature-showcase/` ไว้ แต่ลบรายการลูกโดยตรงอื่น ๆ

## ตัวเลือกทั้งหมด

| ตัวเลือก | ความหมาย |
|---|---|
| `--help` | แสดงความช่วยเหลือจาก command line |
| `--summary` | แสดงเฉพาะสรุป ไม่แสดงชื่อแต่ละรายการ |
| `--apply` | ลบจริง ถ้าไม่ระบุจะเป็น dry-run |
| `--keep NAME` | เก็บรายการลูกโดยตรงของ `artifacts/`; ใช้ซ้ำได้ |
| `--repo-root PATH` | ระบุ repository root อื่นอย่างชัดเจน โดยต้องมี `PATH/artifacts/` |
| `--artifacts-dir PATH` | ระบุโฟลเดอร์ artifacts อย่างชัดเจน แต่ต้องเป็นลูกโดยตรงชื่อ `artifacts` ของ root ที่ระบุ |

โดยทั่วไปไม่จำเป็นต้องใช้ `--repo-root` หรือ `--artifacts-dir` เพราะโปรแกรมค้นหา repository จากตำแหน่งของไฟล์สคริปต์เอง

## ลำดับงานที่แนะนำก่อนรันทดสอบ

```powershell
# 1. ตรวจสอบสิ่งที่จะลบ
python .devbuddy\tools\clear_artifacts.py --summary --keep test-results

# 2. ลบผลลัพธ์เก่าเมื่อยืนยันแล้ว
python .devbuddy\tools\clear_artifacts.py --apply --keep test-results

# 3. รันทดสอบ
dotnet test tests\ReportViewerCore.Rendering.Tests -c Release
```

หากต้องการเก็บ TRX เดิมไว้เพื่อเปรียบเทียบ ให้ใช้ `--keep test-results` หรือใช้ชื่อโฟลเดอร์ผลลัพธ์อื่นที่มีอยู่จริง

## กรณี Excel แจ้งซ่อมไฟล์

เครื่องมือนี้ช่วยลบไฟล์ XLSX เก่าก่อนสร้างใหม่ แต่ไม่ได้แก้โครงสร้าง XLSX เอง หาก Excel แสดงข้อความประมาณว่า “We found a problem with some content” ให้ทำตามลำดับนี้:

1. รัน dry-run และตรวจสอบรายการ
2. รันด้วย `--apply` เพื่อเคลียร์ผลลัพธ์เก่า
3. สร้างไฟล์ใหม่ด้วย sample หรือ test เดิม
4. ตรวจสอบไฟล์ XLSX ที่สร้างใหม่ด้วย validator และเปิดด้วย Excel

สำหรับปัญหาเดิมของโครงการ สาเหตุอยู่ที่ลำดับ child ใน `sheet1.xml` ไม่ถูกต้องและมี theme reference ที่ไม่ครบ ไม่ใช่เพียงไฟล์เก่าค้างอยู่ การแก้ production code และการตรวจสอบ OpenXML ยังจำเป็น

## แก้ปัญหาเบื้องต้น

### โปรแกรมแจ้งว่าไม่พบ `artifacts`

ตรวจสอบว่าเรียกจาก repository ที่ถูกต้อง และไฟล์อยู่ที่:

```text
C:\Codes\reportviewercore\artifacts
```

### โปรแกรมปฏิเสธ `--keep`

`--keep` รับเฉพาะชื่อรายการลูกโดยตรง เช่น `test-results` ไม่รับ path ซ้อน เช่น `test-results\run1.trx`, `..` หรือ absolute path

### ลบไม่ได้เพราะไฟล์ถูกใช้งาน

ปิด Excel, Word, terminal หรือ process ที่กำลังใช้ไฟล์ แล้วเริ่มคำสั่ง `--apply` ใหม่

### ต้องการดูชื่อรายการทีละรายการ

ไม่ต้องใช้ `--summary`:

```powershell
python .devbuddy\tools\clear_artifacts.py --keep test-results
```

โปรแกรมจะแสดง `DRY-RUN`, `KEEP` หรือ `SKIP LINK` ตามรายการที่พบ

## สรุปคำแนะนำ

ควรเก็บไฟล์นี้ไว้ใน repository เพราะใช้ซ้ำได้ก่อนการทดสอบหลายรอบ เริ่มจาก dry-run เสมอ และใช้ `--apply` เฉพาะเมื่อยืนยันรายการที่จะลบแล้ว
