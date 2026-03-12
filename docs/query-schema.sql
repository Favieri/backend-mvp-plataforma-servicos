with user_tables as (
    select
        n.nspname as schema_name,
        c.relname as table_name,
        c.oid as table_oid
    from pg_class c
    join pg_namespace n
        on n.oid = c.relnamespace
    where c.relkind in ('r', 'p')
      and n.nspname = 'public'
),
schema_export as (
    select jsonb_build_object(
        'database', current_database(),
        'schema', 'public',
        'tables',
        coalesce(
            jsonb_agg(
                jsonb_build_object(
                    'table', ut.table_name,

                    'columns',
                    coalesce((
                        select jsonb_agg(
                            jsonb_build_object(
                                'name', a.attname,
                                'type', pg_catalog.format_type(a.atttypid, a.atttypmod),
                                'nullable', not a.attnotnull,
                                'default', pg_get_expr(ad.adbin, ad.adrelid)
                            )
                            order by a.attnum
                        )
                        from pg_attribute a
                        left join pg_attrdef ad
                            on ad.adrelid = a.attrelid
                           and ad.adnum = a.attnum
                        where a.attrelid = ut.table_oid
                          and a.attnum > 0
                          and not a.attisdropped
                    ), '[]'::jsonb),

                    'primary_key',
                    coalesce((
                        select jsonb_agg(att.attname order by k.ord)
                        from pg_constraint con
                        join lateral unnest(con.conkey) with ordinality as k(attnum, ord)
                            on true
                        join pg_attribute att
                            on att.attrelid = con.conrelid
                           and att.attnum = k.attnum
                        where con.conrelid = ut.table_oid
                          and con.contype = 'p'
                    ), '[]'::jsonb),

                    'foreign_keys',
                    coalesce((
                        select jsonb_agg(
                            jsonb_build_object(
                                'name', con.conname,
                                'columns', (
                                    select jsonb_agg(att_src.attname order by src.ord)
                                    from unnest(con.conkey) with ordinality as src(attnum, ord)
                                    join pg_attribute att_src
                                        on att_src.attrelid = con.conrelid
                                       and att_src.attnum = src.attnum
                                ),
                                'references_table', cls_ref.relname,
                                'references_columns', (
                                    select jsonb_agg(att_ref.attname order by ref.ord)
                                    from unnest(con.confkey) with ordinality as ref(attnum, ord)
                                    join pg_attribute att_ref
                                        on att_ref.attrelid = con.confrelid
                                       and att_ref.attnum = ref.attnum
                                )
                            )
                            order by con.conname
                        )
                        from pg_constraint con
                        join pg_class cls_ref
                            on cls_ref.oid = con.confrelid
                        where con.conrelid = ut.table_oid
                          and con.contype = 'f'
                    ), '[]'::jsonb),

                    'indexes',
                    coalesce((
                        select jsonb_agg(
                            jsonb_build_object(
                                'name', i.indexname,
                                'definition', i.indexdef
                            )
                            order by i.indexname
                        )
                        from pg_indexes i
                        where i.schemaname = 'public'
                          and i.tablename = ut.table_name
                    ), '[]'::jsonb)
                )
                order by ut.table_name
            ),
            '[]'::jsonb
        )
    ) as schema_json
    from user_tables ut
)
select jsonb_pretty(schema_json) as schema_export_json
from schema_export;