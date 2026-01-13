ssh root@37.27.189.86 @"
docker exec naia-postgres psql -U naia -d naia -c "SELECT s.id, p.name as pattern_name, s.overall_confidence, s.status, s.created_at FROM pattern_suggestions s JOIN patterns p ON s.pattern_id = p.id ORDER BY s.created_at DESC LIMIT 10;"
"@
