export interface ChatRequest {
  question: string;
  sessionId?: string;
  language?: string;
}

export interface ChatResponse {
  answer: string;
  sources: string[];
  sessionId: string;
}

export interface ChatMessage {
  id: string;
  content: string;
  isUser: boolean;
  timestamp: Date;
  sources?: string[];
}

export interface ApiStats {
  documentChunks: number;
  vectorStore: string;
  embeddingModel: string;
  embeddingDimension?: number;
  timestamp: string;
}

export interface HealthResponse {
  status: string;
  timestamp: string;
}

export interface SourceStat {
  source: string;
  chunkCount: number;
  sourceType: string;
  latestUpdate: string;
}

export interface SourcesResponse {
  totalChunks: number;
  totalSources: number;
  sources: SourceStat[];
  timestamp: string;
}

export interface DeleteResponse {
  message: string;
}

export interface UploadResponse {
  message: string;
}

export interface WebContentRequest {
  url: string;
  includeLinks: boolean;
  maxDepth: number;
  frameworkType?: string; // 'angular' | 'react' | 'vue' | 'static' | null
  customRoutes?: string[]; // User-provided routes
}

export interface WebContentResponse {
  url: string;
  chunksCreated: number;
  status: string;
  processedUrls: string[];
}
