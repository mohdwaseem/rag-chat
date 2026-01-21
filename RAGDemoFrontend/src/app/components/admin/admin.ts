import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { Textarea } from 'primeng/textarea';
import { CheckboxModule } from 'primeng/checkbox';
import { Select } from 'primeng/select';
import { ProgressBarModule } from 'primeng/progressbar';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { DocumentUpload } from '../document-upload/document-upload';
import { ChatService } from '../../services/chat.service';
import { AuthService } from '../../services/auth.service';
import { WebContentRequest, ApiStats, HealthResponse, SourcesResponse } from '../../models/chat.model';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

@Component({
  selector: 'app-admin',
  imports: [
    CommonModule,
    FormsModule,
    CardModule,
    ButtonModule,
    InputTextModule,
    Textarea,
    CheckboxModule,
    Select,
    ProgressBarModule,
    ToastModule,
    DocumentUpload,
    TranslateModule
  ],
  providers: [MessageService],
  templateUrl: './admin.html',
  styleUrl: './admin.scss',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Admin implements OnInit {
  websiteUrl: string = '';
  includeLinks: boolean = false;
  maxDepth: number = 1;
  isIngesting$ = new BehaviorSubject<boolean>(false);
  processedUrls: string[] = [];
  frameworkType: string | null = null;
  customRoutesText: string = '';
  frameworkOptions: any[] = [];
  healthStatus$ = new BehaviorSubject<HealthResponse | null>(null);
  stats$ = new BehaviorSubject<ApiStats | null>(null);
  sources$ = new BehaviorSubject<SourcesResponse | null>(null);
  isLoadingHealth$ = new BehaviorSubject<boolean>(false);
  isLoadingStats$ = new BehaviorSubject<boolean>(false);
  isLoadingSources$ = new BehaviorSubject<boolean>(false);
  isLoadingMe$ = new BehaviorSubject<boolean>(false);
  meInfo$ = new BehaviorSubject<{ username: string; roles: string[]; claims: Array<{ type: string; value: string }> } | null>(null);
  deleteDocumentName = '';
  isDeleting$ = new BehaviorSubject<boolean>(false);

  constructor(
    private chatService: ChatService,
    private authService: AuthService,
    private messageService: MessageService,
    private translate: TranslateService
  ) {
    this.translate.onLangChange.subscribe(() => {
      this.updateFrameworkOptions();
    });
  }

  ngOnInit(): void {
    this.updateFrameworkOptions();
  }

  loadHealth(): void {
    this.isLoadingHealth$.next(true);
    this.chatService.healthCheck().subscribe({
      next: (response) => {
        this.isLoadingHealth$.next(false);
        this.healthStatus$.next(response);
      },
      error: () => {
        this.isLoadingHealth$.next(false);
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('admin.systemStatus.healthErrorTitle'),
          detail: this.translate.instant('admin.systemStatus.healthErrorMessage'),
          life: 4000
        });
      }
    });
  }

  loadStats(): void {
    this.isLoadingStats$.next(true);
    this.chatService.getStats().subscribe({
      next: (response) => {
        this.isLoadingStats$.next(false);
        this.stats$.next(response);
      },
      error: () => {
        this.isLoadingStats$.next(false);
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('admin.systemStatus.statsErrorTitle'),
          detail: this.translate.instant('admin.systemStatus.statsErrorMessage'),
          life: 4000
        });
      }
    });
  }

  loadSources(): void {
    this.isLoadingSources$.next(true);
    this.chatService.getSources().subscribe({
      next: (response) => {
        this.isLoadingSources$.next(false);
        this.sources$.next(response);
      },
      error: () => {
        this.isLoadingSources$.next(false);
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('admin.systemStatus.sourcesErrorTitle'),
          detail: this.translate.instant('admin.systemStatus.sourcesErrorMessage'),
          life: 4000
        });
      }
    });
  }

  loadMe(): void {
    this.isLoadingMe$.next(true);
    this.authService.me().subscribe({
      next: (response) => {
        this.isLoadingMe$.next(false);
        this.meInfo$.next(response);
      },
      error: () => {
        this.isLoadingMe$.next(false);
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('admin.systemStatus.meErrorTitle'),
          detail: this.translate.instant('admin.systemStatus.meErrorMessage'),
          life: 4000
        });
      }
    });
  }

  deleteDocument(): void {
    if (!this.deleteDocumentName.trim()) {
      return;
    }

    this.isDeleting$.next(true);
    const documentName = this.deleteDocumentName.trim();

    this.chatService.deleteDocument(documentName).subscribe({
      next: (response) => {
        this.isDeleting$.next(false);
        this.deleteDocumentName = '';
        this.messageService.add({
          severity: 'success',
          summary: this.translate.instant('admin.deleteDocument.successTitle'),
          detail: response.message,
          life: 4000
        });
      },
      error: (error) => {
        this.isDeleting$.next(false);
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('admin.deleteDocument.errorTitle'),
          detail: error?.error?.message || this.translate.instant('admin.deleteDocument.errorMessage'),
          life: 4000
        });
      }
    });
  }

  onDocumentUploaded(): void {
    this.messageService.add({
      severity: 'success',
      summary: this.translate.instant('admin.documentUpload.successTitle'),
      detail: this.translate.instant('admin.documentUpload.successMessage'),
      life: 5000
    });
  }

  ingestWebsite(): void {
    if (!this.websiteUrl.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: this.translate.instant('admin.websiteIngestion.urlRequiredTitle'),
        detail: this.translate.instant('admin.websiteIngestion.urlRequiredMessage'),
        life: 3000
      });
      return;
    }

    // Validate custom routes if framework is selected
    if (this.frameworkType && this.frameworkType !== 'static' && !this.customRoutesText.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: this.translate.instant('admin.websiteIngestion.routesRequiredTitle'),
        detail: this.translate.instant('admin.websiteIngestion.routesRequiredMessage'),
        life: 3000
      });
      return;
    }

    this.isIngesting$.next(true);
    this.processedUrls = [];

    // Parse custom routes from textarea (comma or newline separated)
    let customRoutes: string[] | undefined = undefined;
    if (this.frameworkType && this.frameworkType !== 'static' && this.customRoutesText.trim()) {
      customRoutes = this.customRoutesText
        .split(/[,\r\n]+/) // Split by comma or newline
        .map(route => route.trim())
        .filter(route => route.length > 0)
        .map(route => route.startsWith('/') ? route : '/' + route); // Ensure routes start with /
    }

    const request: WebContentRequest = {
      url: this.websiteUrl,
      includeLinks: this.includeLinks,
      maxDepth: this.maxDepth,
      frameworkType: this.frameworkType || undefined,
      customRoutes: customRoutes
    };

    this.chatService.ingestUrl(request).subscribe({
      next: (response) => {
        this.isIngesting$.next(false);
        this.processedUrls = response.processedUrls;
        this.messageService.add({
          severity: 'success',
          summary: this.translate.instant('admin.websiteIngestion.successTitle'),
          detail: this.translate.instant('admin.websiteIngestion.successMessage', {
            chunks: response.chunksCreated,
            pages: response.processedUrls.length
          }),
          life: 6000
        });
        this.websiteUrl = '';
        this.customRoutesText = '';
        this.frameworkType = null;
      },
      error: (error) => {
        this.isIngesting$.next(false);
        console.error('Ingestion error:', error);
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('admin.websiteIngestion.ingestionFailedTitle'),
          detail: error.error?.message || this.translate.instant('admin.websiteIngestion.ingestionFailedMessage'),
          life: 5000
        });
      }
    });
  }

  private updateFrameworkOptions(): void {
    this.frameworkOptions = [
      { label: this.translate.instant('admin.websiteIngestion.frameworks.static'), value: 'static' },
      { label: this.translate.instant('admin.websiteIngestion.frameworks.angular'), value: 'angular' },
      { label: this.translate.instant('admin.websiteIngestion.frameworks.react'), value: 'react' },
      { label: this.translate.instant('admin.websiteIngestion.frameworks.vue'), value: 'vue' }
    ];
  }
}
