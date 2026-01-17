import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ChatRequest, ChatResponse, ApiStats, UploadResponse, WebContentRequest, WebContentResponse } from '../models/chat.model';

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
    return this.http.post<UploadResponse>(`${this.apiUrl}/chat/upload`, formData);
  }

  getStats(): Observable<ApiStats> {
    return this.http.get<ApiStats>(`${this.apiUrl}/chat/stats`);
  }

  ingestUrl(request: WebContentRequest): Observable<WebContentResponse> {
    return this.http.post<WebContentResponse>(`${this.apiUrl}/chat/ingest-url`, request);
  }

  healthCheck(): Observable<any> {
    return this.http.get(`${this.apiUrl}/chat/health`);
  }
}
