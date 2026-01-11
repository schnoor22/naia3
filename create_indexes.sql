CREATE INDEX IX_data_sources_is_enabled ON public.data_sources(is_enabled);
CREATE UNIQUE INDEX IX_data_sources_name ON public.data_sources(name);
CREATE INDEX IX_data_sources_source_type ON public.data_sources(source_type);
CREATE INDEX IX_points_data_source_id ON public.points(data_source_id);
CREATE UNIQUE INDEX IX_points_name ON public.points(name);
