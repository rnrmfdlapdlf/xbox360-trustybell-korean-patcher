# TrystybellKoreanPatcher

Xbox 360 일본판 `트러스티 벨` ISO용 한글 패처입니다.

## 현재 범위

- `ImasKoreanPatcher` 구조를 참고한 WinForms UI
- ISO 드래그 앤 드롭 및 파일 선택
- 출력 ISO 경로 선택
- 향후 패치 흐름을 보여 주는 단계 목록
- 순수 C# 패치 파이프라인 인터페이스와 자리표시자 단계

## 예정된 패치 단계

1. ISO 검증
2. 작업 폴더 준비
3. XISO 추출
4. `default.xex` 텍스트/델타 패치
5. BMD/STBL 테이블 패치
6. `.e` BTX fixed/reallocate 패치
7. `p1.fnt` / `p1_g.fnt` 한글 폰트 패치
8. NTEX/DDS 이미지 텍스트 패치
9. ISO 재패킹

## 빌드

Visual Studio 또는 MSBuild로 빌드합니다.

```powershell
%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe TrystybellKoreanPatcher.sln /p:Configuration=Release
```
