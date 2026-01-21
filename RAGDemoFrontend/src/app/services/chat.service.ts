import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ChatRequest, ChatResponse, ApiStats, UploadResponse, WebContentRequest, WebContentResponse, HealthResponse, SourcesResponse, DeleteResponse } from '../models/chat.model';

@Injectable({
  providedIn: 'root'
})
export class ChatService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  askQuestion(request: ChatRequest): Observable<ChatResponse> {
    return this.http.post<ChatResponse>(`${this.apiUrl}/chat/ask`, request);
  }

  uploadDocument(file: File): Observable<UploadResponse> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<UploadResponse>(`${this.apiUrl}/rag/upload`, formData);
  }

  getStats(): Observable<ApiStats> {
    return this.http.get<ApiStats>(`${this.apiUrl}/rag/stats`);
  }

  ingestUrl(request: WebContentRequest): Observable<WebContentResponse> {
    return this.http.post<WebContentResponse>(`${this.apiUrl}/rag/ingest-url`, request);
  }

  healthCheck(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>(`${this.apiUrl}/rag/health`);
  }

  getSources(): Observable<SourcesResponse> {
    return this.http.get<SourcesResponse>(`${this.apiUrl}/rag/sources`);
  }

  deleteDocument(documentName: string): Observable<DeleteResponse> {
    return this.http.delete<DeleteResponse>(`${this.apiUrl}/rag/document/${encodeURIComponent(documentName)}`);
  }
}
